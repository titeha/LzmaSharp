using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

public enum SevenZipFolderDecodeResult
{
  Ok,
  InvalidData,
  NotSupported,
}

/// <summary>
/// <para>Декодирование данных папки (Folder) из 7z.</para>
/// <para>
/// На текущем этапе поддерживаем только простейший вариант:
/// - один coder
/// - один входной поток
/// - один выходной поток
/// - без BindPairs
/// - coder: Copy (0x00) или LZMA2 (0x21)
/// </para>
/// </summary>
public static class SevenZipFolderDecoder
{
  private const byte _methodIdCopy = 0x00;
  private const byte _methodIdLzma2 = 0x21;

  public static SevenZipFolderDecodeResult DecodeFolderToArray(
      SevenZipStreamsInfo streamsInfo,
      ReadOnlySpan<byte> packedStreams,
      int folderIndex,
      out byte[] output)
  {
    output = [];

    ArgumentNullException.ThrowIfNull(streamsInfo);

    if (streamsInfo.PackInfo is not { } packInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if (streamsInfo.UnpackInfo is not { } unpackInfo)
      return SevenZipFolderDecodeResult.InvalidData;

    if ((uint)folderIndex >= (uint)unpackInfo.Folders.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipFolder folder = unpackInfo.Folders[folderIndex];

    // Пока поддерживаем только максимально простой вариант.
    if (folder.Coders.Length != 1)
      return SevenZipFolderDecodeResult.NotSupported;

    if (folder.BindPairs.Length != 0)
      return SevenZipFolderDecodeResult.NotSupported;

    if (folder.PackedStreamIndices.Length != 1)
      return SevenZipFolderDecodeResult.NotSupported;

    // Размеры распаковки в 7z лежат не в самом Folder, а отдельным массивом в UnpackInfo.
    if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (folderUnpackSizes is null || folderUnpackSizes.Length == 0)
      return SevenZipFolderDecodeResult.InvalidData;

    // В 7z folder хранит индексы packed streams внутри себя, а pack streams в PackInfo идут подряд
    // по всем folder'ам. Поэтому глобальный индекс pack stream для текущего folder'а вычисляем как
    // сумму NumPackedStreams у всех предыдущих folder'ов.
    //
    // Сейчас поддерживаем только простейший вариант: у folder ровно один packed stream.
    if (folder.PackedStreamIndices[0] != 0)
      return SevenZipFolderDecodeResult.NotSupported;

    ulong packStreamIndexU64 = 0;
    for (int i = 0; i < folderIndex; i++)
      packStreamIndexU64 += (ulong)unpackInfo.Folders[i].PackedStreamIndices.Length;

    if (packStreamIndexU64 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    uint packStreamIndex = (uint)packStreamIndexU64;
    if (packStreamIndex >= (uint)packInfo.PackSizes.Length)
      return SevenZipFolderDecodeResult.InvalidData;

    if (!TryGetPackStream(packInfo, packedStreams, packStreamIndex, out ReadOnlySpan<byte> packStream))
      return SevenZipFolderDecodeResult.InvalidData;

    SevenZipCoderInfo coder = folder.Coders[0];

    ulong unpackSizeU64 = folderUnpackSizes[^1];
    if (unpackSizeU64 > int.MaxValue)
      return SevenZipFolderDecodeResult.NotSupported;

    int expectedUnpackSize = (int)unpackSizeU64;

    if (IsSingleByteMethodId(coder.MethodId, _methodIdCopy))
    {
      // Copy: pack-stream содержит распакованные данные.
      output = packStream.ToArray();
      return output.Length == expectedUnpackSize
          ? SevenZipFolderDecodeResult.Ok
          : SevenZipFolderDecodeResult.InvalidData;
    }

    if (IsSingleByteMethodId(coder.MethodId, _methodIdLzma2))
    {
      if (coder.Properties is null || coder.Properties.Length != 1)
        return SevenZipFolderDecodeResult.InvalidData;

      byte lzma2PropertiesByte = coder.Properties[0];

      Lzma2DecodeResult decodeResult = Lzma2Decoder.DecodeToArray(
          input: packStream,
          dictionaryProp: lzma2PropertiesByte,
          output: out output,
          bytesConsumed: out int bytesConsumed);

      // Явные ошибки пробрасываем как есть.
      if (decodeResult == Lzma2DecodeResult.NotSupported)
      {
        output = [];
        return SevenZipFolderDecodeResult.NotSupported;
      }

      if (decodeResult == Lzma2DecodeResult.InvalidData)
      {
        output = [];
        return SevenZipFolderDecodeResult.InvalidData;
      }

      // Для 7z ожидаемый размер известен из хедера, поэтому валидируем по нему.
      if (output.Length != expectedUnpackSize)
      {
        output = [];
        return SevenZipFolderDecodeResult.InvalidData;
      }

      if ((uint)bytesConsumed > (uint)packStream.Length)
      {
        output = [];
        return SevenZipFolderDecodeResult.InvalidData;
      }

      // Иногда декодер может остановиться сразу после записи последнего байта,
      // не «съев» финальный 0x00 (End marker) в LZMA2.
      // Это приемлемо, если хвост содержит только нули.
      if (bytesConsumed != packStream.Length)
      {
        ReadOnlySpan<byte> tail = packStream[bytesConsumed..];
        for (int i = 0; i < tail.Length; i++)
          if (tail[i] != 0)
          {
            output = [];
            return SevenZipFolderDecodeResult.InvalidData;
          }
      }

      return SevenZipFolderDecodeResult.Ok;
    }

    return SevenZipFolderDecodeResult.NotSupported;
  }

  private static bool IsSingleByteMethodId(byte[] methodId, byte expected)
      => methodId.Length == 1 && methodId[0] == expected;

  private static bool TryGetPackStream(
      SevenZipPackInfo packInfo,
      ReadOnlySpan<byte> packedStreams,
      uint packStreamIndex,
      out ReadOnlySpan<byte> packStream)
  {
    packStream = default;

    // Ограничим поддержку: индекс должен помещаться в int,
    // иначе Slice всё равно не сможет адресовать такие значения.
    if (packStreamIndex > int.MaxValue)
      return false;

    ulong start = packInfo.PackPos;

    // При packStreamIndex == 0 цикл не выполнится.
    for (int i = 0; i < (int)packStreamIndex; i++)
      start += packInfo.PackSizes[i];

    ulong size = packInfo.PackSizes[(int)packStreamIndex];

    if (start > (ulong)packedStreams.Length)
      return false;

    if (size > (ulong)packedStreams.Length - start)
      return false;

    if (start > int.MaxValue || size > int.MaxValue)
      return false;

    packStream = packedStreams.Slice((int)start, (int)size);
    return true;
  }
}
