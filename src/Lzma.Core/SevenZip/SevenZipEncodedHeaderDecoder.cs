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

    // В 7z после NID.EncodedHeader идёт StreamsInfo (без отдельного маркера NID).
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

    if (streamsInfo.PackInfo is null || streamsInfo.UnpackInfo is null)
      return SevenZipArchiveReadResult.InvalidData;

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo;

    // На этапе 1 поддерживаем только один packed stream у EncodedHeader.
    if (packInfo.PackSizes.Length == 0)
      return SevenZipArchiveReadResult.InvalidData;

    if (packInfo.PackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    ulong packPos = packInfo.PackPos;
    ulong packSize = packInfo.PackSizes[0];

    // Проверки границ, т.к. внутри будет Slice() по int.
    if (packPos > (ulong)packedStreams.Length)
      return SevenZipArchiveReadResult.InvalidData;

    if (packSize > (ulong)packedStreams.Length - packPos)
      return SevenZipArchiveReadResult.InvalidData;

    if (packPos > int.MaxValue || packSize > int.MaxValue || packPos + packSize > int.MaxValue)
      return SevenZipArchiveReadResult.NotSupported;

    // Пока поддерживаем ровно одну папку (folder) и один output stream (header bytes).
    if (unpackInfo.Folders.Length != 1 || unpackInfo.FolderUnpackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[0];
    if (folderUnpackSizes is null || folderUnpackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    // Декодируем packed stream EncodedHeader через общий декодер folder'ов.
    SevenZipFolderDecodeResult folderDecodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: streamsInfo,
      packedStreams: packedStreams,
      folderIndex: 0,
      output: out decodedHeaderBytes);

    if (folderDecodeResult != SevenZipFolderDecodeResult.Ok)
    {
      decodedHeaderBytes = [];
      return folderDecodeResult == SevenZipFolderDecodeResult.NotSupported
        ? SevenZipArchiveReadResult.NotSupported
        : SevenZipArchiveReadResult.InvalidData;
    }

    // Парсим обычный Header из распакованных байт.
    switch (SevenZipHeaderReader.TryRead(decodedHeaderBytes, out decodedHeader, out int headerBytesConsumed))
    {
      case SevenZipHeaderReadResult.Ok:
        break;

      case SevenZipHeaderReadResult.NeedMoreInput:
        // Декодированные байты уже целиком в памяти: если парсер просит ещё — значит заголовок битый.
        return SevenZipArchiveReadResult.InvalidData;

      case SevenZipHeaderReadResult.NotSupported:
        return SevenZipArchiveReadResult.NotSupported;

      default:
        return SevenZipArchiveReadResult.InvalidData;
    }

    if (headerBytesConsumed != decodedHeaderBytes.Length)
      return SevenZipArchiveReadResult.InvalidData;

    return SevenZipArchiveReadResult.Ok;
  }
}
