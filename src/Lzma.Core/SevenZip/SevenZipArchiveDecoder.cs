namespace Lzma.Core.SevenZip;

public enum SevenZipArchiveDecodeResult
{
  Ok,
  NeedMoreData,
  InvalidData,
  NotSupported,
}

public static class SevenZipArchiveDecoder
{
  /// <summary>
  /// <para>Декодирует 7z-архив целиком и возвращает массив файлов (имя + содержимое).</para>
  /// <para>
  /// Ограничения текущей реализации (на этом шаге):
  /// - Поддерживается только архив с 1 folder (одна цепочка кодеров).
  /// - Поддерживается только 1 pack stream.
  /// - Поддерживаются только файлы с потоками (без EmptyStreams).
  /// </para>
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeAllFilesToArray(ReadOnlySpan<byte> archive, out SevenZipDecodedFile[] files)
    => DecodeAllFilesToArray(archive, out files, out _);

  /// <summary>
  /// То же самое, что <see cref="DecodeAllFilesToArray(ReadOnlySpan{byte}, out SevenZipDecodedFile[])"/>,
  /// но дополнительно возвращает количество байт, потреблённых ридером.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeAllFilesToArray(
    ReadOnlySpan<byte> archive,
    out SevenZipDecodedFile[] files,
    out int bytesConsumed)
  {
    files = [];

    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult readResult = reader.Read(archive, out bytesConsumed);

    if (readResult == SevenZipArchiveReadResult.NeedMoreInput)
      return SevenZipArchiveDecodeResult.NeedMoreData;

    if (readResult != SevenZipArchiveReadResult.Ok)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (!reader.Header.HasValue)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipHeader header = reader.Header.Value;

    // StreamsInfo в нашем API не Nullable, но в целях "защиты от некорректных данных"
    // проверим на null всё равно.
    SevenZipStreamsInfo? streamsInfoRef = header.StreamsInfo;
    if (streamsInfoRef is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipStreamsInfo streamsInfo = streamsInfoRef;

    if (!streamsInfo.PackInfo.HasValue)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;

    SevenZipUnpackInfo? unpackInfoRef = streamsInfo.UnpackInfo;
    if (unpackInfoRef is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipUnpackInfo unpackInfo = unpackInfoRef;

    SevenZipFolder[] folders = unpackInfo.Folders;
    if (folders.Length != 1)
      return SevenZipArchiveDecodeResult.NotSupported;

    // Пока поддерживаем только 1 pack stream.
    if (packInfo.PackSizes.Length != 1)
      return SevenZipArchiveDecodeResult.NotSupported;

    int fileCount = (int)header.FilesInfo.FileCount;
    if (fileCount <= 0)
      return SevenZipArchiveDecodeResult.NotSupported;

    if (!TryGetFileSizes(streamsInfo, unpackInfo, folderIndex: 0, fileCount, out ulong[] fileSizes))
      return SevenZipArchiveDecodeResult.NotSupported;

    if (fileSizes.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    // Декодируем folder (все sub-stream'ы идут подряд).
    SevenZipFolderDecodeResult folderDecode = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo,
      reader.PackedStreams.Span,
      folderIndex: 0,
      out byte[] unpacked);

    if (folderDecode == SevenZipFolderDecodeResult.NotSupported)
      return SevenZipArchiveDecodeResult.NotSupported;

    if (folderDecode != SevenZipFolderDecodeResult.Ok)
      return SevenZipArchiveDecodeResult.InvalidData;

    // Имена файлов (если нет имён — создадим заглушки).
    string[] names = header.FilesInfo.Names ?? BuildFallbackNames(fileCount);
    if (names.Length != fileCount)
      names = BuildFallbackNames(fileCount);

    List<SevenZipDecodedFile> result = new(capacity: fileCount);

    int cursor = 0;
    for (int i = 0; i < fileCount; i++)
    {
      ulong sizeU64 = fileSizes[i];
      if (sizeU64 > int.MaxValue)
        return SevenZipArchiveDecodeResult.NotSupported;

      int size = (int)sizeU64;

      if ((uint)cursor > (uint)unpacked.Length)
        return SevenZipArchiveDecodeResult.InvalidData;

      if (cursor + size > unpacked.Length)
        return SevenZipArchiveDecodeResult.InvalidData;

      byte[] bytes = new byte[size];
      unpacked.AsSpan(cursor, size).CopyTo(bytes);

      result.Add(new SevenZipDecodedFile(names[i], bytes));
      cursor += size;
    }

    // Если остались лишние байты — на данном шаге считаем это некорректными данными.
    if (cursor != unpacked.Length)
      return SevenZipArchiveDecodeResult.InvalidData;

    files = [.. result];
    return SevenZipArchiveDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует 7z-архив с ОДНИМ файлом и возвращает его содержимое.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(
    ReadOnlySpan<byte> archive,
    out byte[] fileBytes,
    out string? fileName,
    out int bytesConsumed)
  {
    fileBytes = [];
    fileName = null;

    SevenZipArchiveDecodeResult r = DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files, out bytesConsumed);
    if (r != SevenZipArchiveDecodeResult.Ok)
      return r;

    if (files.Length != 1)
      return SevenZipArchiveDecodeResult.NotSupported;

    fileName = files[0].Name;
    fileBytes = files[0].Bytes;
    return SevenZipArchiveDecodeResult.Ok;
  }

  private static string[] BuildFallbackNames(int fileCount)
  {
    string[] names = new string[fileCount];
    for (int i = 0; i < fileCount; i++)
      names[i] = $"file{i}.bin";

    return names;
  }

  private static bool TryGetFileSizes(
    SevenZipStreamsInfo streamsInfo,
    SevenZipUnpackInfo unpackInfo,
    int folderIndex,
    int fileCount,
    out ulong[] fileSizes)
  {
    // Для 1 файла без SubStreamsInfo размер берём из folder unpack size (1 out stream).
    if (fileCount == 1 && streamsInfo.SubStreamsInfo is null)
    {
      if ((uint)folderIndex >= (uint)unpackInfo.FolderUnpackSizes.Length)
      {
        fileSizes = [];
        return false;
      }

      ulong[] outSizes = unpackInfo.FolderUnpackSizes[folderIndex];
      if (outSizes is null || outSizes.Length != 1)
      {
        fileSizes = [];
        return false;
      }

      fileSizes = [outSizes[0]];
      return true;
    }

    SevenZipSubStreamsInfo? subStreamsInfo = streamsInfo.SubStreamsInfo;
    if (subStreamsInfo is null)
    {
      fileSizes = [];
      return false;
    }

    if ((uint)folderIndex >= (uint)subStreamsInfo.UnpackSizesPerFolder.Length)
    {
      fileSizes = [];
      return false;
    }

    ulong[] sizesForFolder = subStreamsInfo.UnpackSizesPerFolder[folderIndex];
    if (sizesForFolder is null)
    {
      fileSizes = [];
      return false;
    }

    if (sizesForFolder.Length != fileCount)
    {
      fileSizes = [];
      return false;
    }

    fileSizes = sizesForFolder;
    return true;
  }
}
