using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

internal static class SevenZipEncodedHeaderDecoder
{
  public static SevenZipArchiveReadResult TryDecode(
      ReadOnlySpan<byte> nextHeaderBytes,
      ReadOnlySpan<byte> packedStreams,
      out byte[] decodedHeaderBytes,
      out SevenZipHeader decodedHeader)
  {
    decodedHeaderBytes = [];
    decodedHeader = default;

    if (nextHeaderBytes.IsEmpty)
      return SevenZipArchiveReadResult.InvalidData;

    if (nextHeaderBytes[0] != SevenZipNid.EncodedHeader)
      return SevenZipArchiveReadResult.InvalidData;

    // В 7z после NID.EncodedHeader идёт структура StreamsInfo (без отдельного маркера NID). Она начинается
    // непосредственно с PackInfo/UnpackInfo/... и заканчивается NID.End.
    const int offset = 1;

    var streamsInfoRead = SevenZipStreamsInfoReader.TryRead(
        nextHeaderBytes[offset..],
        out SevenZipStreamsInfo streamsInfo,
        out _);

    switch (streamsInfoRead)
    {
      case SevenZipStreamsInfoReadResult.Ok:
        break;
      case SevenZipStreamsInfoReadResult.NeedMoreInput:
        return SevenZipArchiveReadResult.NeedMoreInput;
      case SevenZipStreamsInfoReadResult.NotSupported:
        return SevenZipArchiveReadResult.NotSupported;
      default:
        return SevenZipArchiveReadResult.InvalidData;
    }

    // Для EncodedHeader в рамках наших тестов ожидаем PackInfo + UnpackInfo.
    if (streamsInfo.PackInfo is null || streamsInfo.UnpackInfo is null)
      return SevenZipArchiveReadResult.InvalidData;

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo;
    // Пока поддерживаем только один packed stream.
    // В 7z спецификации количество pack streams соответствует длине массива PackSizes.
    if (packInfo.PackSizes.Length == 0)
      return SevenZipArchiveReadResult.InvalidData;

    if (packInfo.PackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;
    ulong packPos = packInfo.PackPos;
    ulong packSize = packInfo.PackSizes[0];

    // Проверки границ, т.к. дальше Slice() работает с int.
    if (packPos > (ulong)packedStreams.Length)
      return SevenZipArchiveReadResult.InvalidData;

    if (packSize > (ulong)packedStreams.Length - packPos)
      return SevenZipArchiveReadResult.InvalidData;

    if (packPos > int.MaxValue || packSize > int.MaxValue || packPos + packSize > int.MaxValue)
      return SevenZipArchiveReadResult.NotSupported;

    ReadOnlySpan<byte> payload = packedStreams.Slice((int)packPos, (int)packSize);

    // Пока поддерживаем ровно одну папку (folder) и один output stream.
    if (unpackInfo.Folders.Length != 1 || unpackInfo.FolderUnpackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    SevenZipFolder folder = unpackInfo.Folders[0];

    ReadOnlySpan<ulong> folderUnpackSizes = unpackInfo.FolderUnpackSizes[0].AsSpan();
    if (folderUnpackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    ulong unpackSize = folderUnpackSizes[0];
    if (unpackSize > int.MaxValue)
      return SevenZipArchiveReadResult.NotSupported;

    // В рамках наших тестов допускаем только один coder (LZMA2).
    ReadOnlySpan<SevenZipCoderInfo> coders = folder.Coders.AsSpan();
    if (coders.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    SevenZipCoderInfo coder = coders[0];

    if (!IsLzma2Method(coder.MethodId))
      return SevenZipArchiveReadResult.NotSupported;

    if (coder.Properties is null || coder.Properties.Length < 1)
      return SevenZipArchiveReadResult.InvalidData;

    byte lzma2PropsByte = coder.Properties[0];
    if (!Lzma2Properties.TryParse(lzma2PropsByte, out Lzma2Properties props))
      return SevenZipArchiveReadResult.InvalidData;

    if (!props.TryGetDictionarySizeInt32(out int dictionarySize))
      return SevenZipArchiveReadResult.NotSupported;

    var decodeResult = Lzma2Decoder.DecodeToArray(payload, dictionarySize, out decodedHeaderBytes, out int bytesConsumed);
    if (decodeResult != Lzma2DecodeResult.Finished)
      return decodeResult switch
      {
        Lzma2DecodeResult.NeedMoreInput => SevenZipArchiveReadResult.NeedMoreInput,
        Lzma2DecodeResult.NotSupported => SevenZipArchiveReadResult.NotSupported,
        _ => SevenZipArchiveReadResult.InvalidData,
      };

    if (bytesConsumed > payload.Length)
      return SevenZipArchiveReadResult.InvalidData;

    if (bytesConsumed != payload.Length)
    {
      ReadOnlySpan<byte> tail = payload[bytesConsumed..];
      for (int i = 0; i < tail.Length; i++)
        if (tail[i] != 0)
          return SevenZipArchiveReadResult.InvalidData;
    }

    if (decodedHeaderBytes.Length != (int)unpackSize)
      return SevenZipArchiveReadResult.InvalidData;

    switch (SevenZipHeaderReader.TryRead(decodedHeaderBytes, out decodedHeader, out int headerBytesConsumed))
    {
      case SevenZipHeaderReadResult.Ok:
        break;
      case SevenZipHeaderReadResult.NeedMoreInput:
        // Декодированные байты уже целиком в памяти: если парсер просит ещё — значит заголовок битый.
        return SevenZipArchiveReadResult.InvalidData;
      default:
        return SevenZipArchiveReadResult.InvalidData;
    }

    if (headerBytesConsumed != decodedHeaderBytes.Length)
      return SevenZipArchiveReadResult.InvalidData;

    return SevenZipArchiveReadResult.Ok;
  }

  private static bool IsLzma2Method(ReadOnlySpan<byte> methodId)
      => methodId.Length == 1 && methodId[0] == 0x21;
}
