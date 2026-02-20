namespace Lzma.Core.SevenZip;

public static class SevenZipArchiveDecoder
{
  /// <summary>
  /// <para>Декодирует 7z-архив (в памяти) и возвращает все файлы в виде массива (имя + байты).</para>
  /// <para>
  /// Текущая реализация рассчитана на «простой» 7z, который генерируют наши тесты:
  /// - Только 1 входной поток на folder (NumInStreams = 1)
  /// - Только 1 выходной поток на coder (NumOutStreams = 1)
  /// - LZMA2 (включая COPY-режим)
  /// </para>
  /// </summary>

  /// <summary>
  /// Декодирует 7z-архив, содержащий ровно один файл.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(ReadOnlySpan<byte> archiveBytes, out byte[] fileBytes, out string fileName)
  {
    SevenZipArchiveDecodeResult r = DecodeToArray(archiveBytes, out SevenZipDecodedFile[] decodedFiles);
    if (r != SevenZipArchiveDecodeResult.Ok)
    {
      fileBytes = [];
      fileName = string.Empty;
      return r;
    }

    if (decodedFiles.Length != 1)
    {
      fileBytes = [];
      fileName = string.Empty;
      return SevenZipArchiveDecodeResult.NotSupported;
    }

    fileBytes = decodedFiles[0].Bytes;
    fileName = decodedFiles[0].Name;
    return SevenZipArchiveDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует 7z-архив, содержащий ровно один файл.
  /// </summary>
  /// <remarks>
  /// Этот перегруженный метод оставлен для совместимости с тестами/внешним кодом,
  /// которому важно знать, сколько байт входа было обработано.
  /// </remarks>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(
    ReadOnlySpan<byte> archiveBytes,
    out byte[] fileBytes,
    out string fileName,
    out int bytesConsumed)
  {
    SevenZipArchiveDecodeResult r = DecodeToArray(archiveBytes, out SevenZipDecodedFile[] decodedFiles, out bytesConsumed);

    if (r != SevenZipArchiveDecodeResult.Ok)
    {
      fileBytes = [];
      fileName = string.Empty;
      return r;
    }

    if (decodedFiles.Length != 1)
    {
      fileBytes = [];
      fileName = string.Empty;
      return SevenZipArchiveDecodeResult.NotSupported;
    }

    fileBytes = decodedFiles[0].Bytes;
    fileName = decodedFiles[0].Name;
    return SevenZipArchiveDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует 7z-архив и возвращает все файлы.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeAllFilesToArray(ReadOnlySpan<byte> archiveBytes, out SevenZipDecodedFile[] files)
    => DecodeToArray(archiveBytes, out files);

  public static SevenZipArchiveDecodeResult DecodeToArray(ReadOnlySpan<byte> archive, out SevenZipDecodedFile[] files)
      => DecodeToArray(archive, out files, out _);

  /// <summary>
  /// То же самое, но дополнительно возвращает количество байт, потреблённых парсером заголовка 7z.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeToArray(
    ReadOnlySpan<byte> archive,
    out SevenZipDecodedFile[] files,
    out int bytesConsumed)
  {
    files = [];

    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult read = reader.Read(archive, out bytesConsumed);

    if (read == SevenZipArchiveReadResult.NeedMoreInput)
      return SevenZipArchiveDecodeResult.NeedMoreData;
    if (read == SevenZipArchiveReadResult.InvalidData)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (read == SevenZipArchiveReadResult.NotSupported)
      return SevenZipArchiveDecodeResult.NotSupported;
    if (read != SevenZipArchiveReadResult.Ok)
      return SevenZipArchiveDecodeResult.InternalError;

    // В разных шагах эволюции проекта Header встречался и как SevenZipHeader,
    // и как SevenZipHeader? — приводим к nullable, чтобы код оставался устойчивым.
    SevenZipHeader? header = reader.Header;
    if (!header.HasValue)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipFilesInfo filesInfo = header.Value.FilesInfo;

    // Пустой архив: файлов нет, потоков может не быть.
    if (filesInfo.FileCount == 0)
    {
      files = [];
      return SevenZipArchiveDecodeResult.Ok;
    }

    SevenZipStreamsInfo streamsInfo = header.Value.StreamsInfo;
    if (streamsInfo is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipUnpackInfo? unpackInfo = streamsInfo.UnpackInfo;
    if (streamsInfo.PackInfo is null || unpackInfo is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (filesInfo.FileCount > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int fileCount = (int)filesInfo.FileCount;

    string[] names;
    if (filesInfo.Names is null)
    {
      // В 7z kName может отсутствовать. Чтобы не падать на валидных архивах,
      // генерируем технические имена.
      names = new string[fileCount];
      for (int i = 0; i < fileCount; i++)
        names[i] = $"file_{i}";
    }
    else
    {
      if (filesInfo.Names.Length != fileCount)
        return SevenZipArchiveDecodeResult.InvalidData;

      names = filesInfo.Names;
    }

    // kEmptyStream уже распарсен в FilesInfoReader; здесь только валидируем длину.
    bool[]? emptyStreams = filesInfo.EmptyStreams;
    if (emptyStreams is not null && emptyStreams.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    // ---- Подготавливаем «карту» потоков распаковки: folder -> набор unpack-стримов и их размеры.

    int folderCount = unpackInfo.Folders.Length;
    if (folderCount <= 0)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipSubStreamsInfo? sub = streamsInfo.SubStreamsInfo;

    ulong[] numUnpackStreamsPerFolder;
    ulong[][] unpackSizesPerFolder;

    if (sub is not null)
    {
      numUnpackStreamsPerFolder = sub.NumUnpackStreamsPerFolder;
      unpackSizesPerFolder = sub.UnpackSizesPerFolder;

      if (numUnpackStreamsPerFolder.Length != folderCount)
        return SevenZipArchiveDecodeResult.InvalidData;
      if (unpackSizesPerFolder.Length != folderCount)
        return SevenZipArchiveDecodeResult.InvalidData;
    }
    else
    {
      // Если SubStreamsInfo отсутствует, считаем что на каждый folder приходится ровно 1 распакованный поток
      // с размером = общий размер распаковки folder'а.
      numUnpackStreamsPerFolder = new ulong[folderCount];
      unpackSizesPerFolder = new ulong[folderCount][];

      if (unpackInfo.FolderUnpackSizes.Length != folderCount)
        return SevenZipArchiveDecodeResult.InvalidData;

      for (int i = 0; i < folderCount; i++)
      {
        numUnpackStreamsPerFolder[i] = 1;

        ulong[] folderSizes = unpackInfo.FolderUnpackSizes[i];
        if (folderSizes is null || folderSizes.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        unpackSizesPerFolder[i] = [folderSizes[0]];
      }
    }

    // В 7z количество unpack-стримов обычно НЕ равно количеству файлов:
    // kEmptyStream описывает файлы без потока данных.
    ulong totalUnpackStreamsU64 = 0;
    for (int i = 0; i < folderCount; i++)
      totalUnpackStreamsU64 += numUnpackStreamsPerFolder[i];

    if (totalUnpackStreamsU64 > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int totalUnpackStreams = (int)totalUnpackStreamsU64;

    // Считаем количество НЕ-пустых файлов.
    int nonEmptyFilesCount = fileCount;
    if (emptyStreams is not null)
    {
      int cnt = 0;
      for (int i = 0; i < fileCount; i++)
        if (!emptyStreams[i])
          cnt++;

      nonEmptyFilesCount = cnt;
    }

    if (totalUnpackStreams != nonEmptyFilesCount)
      return SevenZipArchiveDecodeResult.NotSupported;

    ReadOnlySpan<byte> packed = reader.PackedStreams.Span;

    List<SevenZipDecodedFile> decoded = new(fileCount);

    int fileIndex = 0;

    for (int folderIndex = 0; folderIndex < folderCount; folderIndex++)
    {
      SevenZipFolderDecodeResult folderRes = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packed,
        folderIndex,
        out byte[] folderUnpacked);

      if (folderRes == SevenZipFolderDecodeResult.InvalidData)
        return SevenZipArchiveDecodeResult.InvalidData;
      if (folderRes == SevenZipFolderDecodeResult.NotSupported)
        return SevenZipArchiveDecodeResult.NotSupported;
      if (folderRes != SevenZipFolderDecodeResult.Ok)
        return SevenZipArchiveDecodeResult.InternalError;

      ulong expectedStreamsU64 = numUnpackStreamsPerFolder[folderIndex];
      if (expectedStreamsU64 > int.MaxValue)
        return SevenZipArchiveDecodeResult.NotSupported;
      int expectedStreams = (int)expectedStreamsU64;

      ulong[] sizes = unpackSizesPerFolder[folderIndex];
      if (sizes is null || sizes.Length != expectedStreams)
        return SevenZipArchiveDecodeResult.InvalidData;

      int cursor = 0;

      for (int s = 0; s < expectedStreams; s++)
      {
          // Пропускаем файлы без потока (kEmptyStream).
          while (emptyStreams is not null &&
                 fileIndex < fileCount &&
                 emptyStreams[fileIndex])
          {
            decoded.Add(new SevenZipDecodedFile(names[fileIndex], []));
            fileIndex++;
          }

          if (fileIndex >= fileCount)
            return SevenZipArchiveDecodeResult.InvalidData;

          ulong sizeU64 = sizes[s];
        if (sizeU64 > int.MaxValue)
          return SevenZipArchiveDecodeResult.NotSupported;
        int size = (int)sizeU64;

        if (size > folderUnpacked.Length - cursor)
          return SevenZipArchiveDecodeResult.InvalidData;

        byte[] fileBytes = new byte[size];
        Array.Copy(folderUnpacked, cursor, fileBytes, 0, size);
        cursor += size;

        decoded.Add(new SevenZipDecodedFile(names[fileIndex], fileBytes));
        fileIndex++;
      }

      // Лишние байты после разбиения по SubStreamsInfo считаем ошибкой формата.
      if (cursor != folderUnpacked.Length)
        return SevenZipArchiveDecodeResult.InvalidData;
    }

    // Если в конце остались файлы без потока (kEmptyStream), возвращаем их как пустые.
    while (emptyStreams is not null &&
           fileIndex < fileCount &&
           emptyStreams[fileIndex])
    {
      decoded.Add(new SevenZipDecodedFile(names[fileIndex], []));
      fileIndex++;
    }

    if (fileIndex != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    files = [.. decoded];
    return SevenZipArchiveDecodeResult.Ok;
  }
}
