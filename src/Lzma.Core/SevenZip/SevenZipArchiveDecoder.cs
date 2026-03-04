using Lzma.Core.Checksums;

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

    // kAnti: элементы "удаления" (обычно в update-архивах). На этапе 1 не поддерживаем.
    bool[]? anti = filesInfo.Anti;
    if (anti is not null && anti.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (anti is not null)
    {
      for (int i = 0; i < fileCount; i++)
      {
        if (!anti[i])
          continue;

        // Anti допустим только для EmptyStream элементов.
        if (emptyStreams?[i] != true)
          return SevenZipArchiveDecodeResult.InvalidData;

        return SevenZipArchiveDecodeResult.NotSupported;
      }
    }

    // FilesInfo.kCRC: CRC32 по файлам (нужно для EmptyStream)
    bool[]? fileCrcDefined = filesInfo.CrcDefined;
    uint[]? fileCrc = filesInfo.Crc;

    if ((fileCrcDefined is null) != (fileCrc is null))
      return SevenZipArchiveDecodeResult.InvalidData;

    if (fileCrcDefined is not null && fileCrcDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (fileCrc is not null && fileCrc.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    uint emptyStreamCrc = Crc32.Compute([]);

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

    if (nonEmptyFilesCount == 0)
    {
      var decodedEmpty = new SevenZipDecodedFile[fileCount];

      for (int i = 0; i < fileCount; i++)
      {
        if (fileCrcDefined?[i] == true && fileCrc![i] != emptyStreamCrc)
          return SevenZipArchiveDecodeResult.InvalidData;

        decodedEmpty[i] = new SevenZipDecodedFile(names[i], []);
      }

      files = decodedEmpty;
      return SevenZipArchiveDecodeResult.Ok;
    }

    SevenZipStreamsInfo streamsInfo = header.Value.StreamsInfo;
    if (streamsInfo is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipUnpackInfo? unpackInfo = streamsInfo.UnpackInfo;
    if (streamsInfo.PackInfo is null || unpackInfo is null)
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

        SevenZipFolder folder = unpackInfo.Folders[i];

        SevenZipArchiveDecodeResult sizeRes = TryGetFolderFinalOutSize(folder, folderSizes, out ulong finalSize);
        if (sizeRes != SevenZipArchiveDecodeResult.Ok)
          return sizeRes;

        unpackSizesPerFolder[i] = [finalSize];
      }
    }

    // CRC: folder-level (UnpackInfo.kCRC)
    bool[]? folderCrcDefined = unpackInfo.FolderCrcDefined;
    uint[]? folderCrc = unpackInfo.FolderCrc;

    if (folderCrcDefined is null != folderCrc is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (folderCrcDefined is not null && folderCrcDefined.Length != folderCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (folderCrc is not null && folderCrc.Length != folderCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    // CRC: stream-level (SubStreamsInfo.kCRC)
    bool[][]? unpackCrcDefinedPerFolder = sub?.UnpackCrcDefinedPerFolder;
    uint[][]? unpackCrcPerFolder = sub?.UnpackCrcPerFolder;

    if (unpackCrcDefinedPerFolder is null != unpackCrcPerFolder is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (unpackCrcDefinedPerFolder is not null && unpackCrcDefinedPerFolder.Length != folderCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (unpackCrcPerFolder is not null && unpackCrcPerFolder.Length != folderCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    // В 7z количество unpack-стримов обычно НЕ равно количеству файлов:
    // kEmptyStream описывает файлы без потока данных.
    ulong totalUnpackStreamsU64 = 0;
    for (int i = 0; i < folderCount; i++)
      totalUnpackStreamsU64 += numUnpackStreamsPerFolder[i];

    if (totalUnpackStreamsU64 > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int totalUnpackStreams = (int)totalUnpackStreamsU64;

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

      if (folderCrcDefined?[folderIndex] == true)
      {
        uint actualFolderCrc = Crc32.Compute(folderUnpacked.AsSpan());
        if (actualFolderCrc != folderCrc![folderIndex])
          return SevenZipArchiveDecodeResult.InvalidData;
      }

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
        while (emptyStreams is not null && fileIndex < fileCount && emptyStreams[fileIndex])
        {
          if (fileCrcDefined?[fileIndex] == true && fileCrc![fileIndex] != emptyStreamCrc)
            return SevenZipArchiveDecodeResult.InvalidData;

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

        // Валидация CRC32 (если задана).
        bool hasExpectedCrc = false;
        uint expectedCrc = 0;

        // 1) CRC на уровне unpack-stream (SubStreamsInfo.kCRC)
        if (unpackCrcDefinedPerFolder is not null)
        {
          bool[] def = unpackCrcDefinedPerFolder[folderIndex];
          uint[] crc = unpackCrcPerFolder![folderIndex];

          if (def is null || crc is null || def.Length != expectedStreams || crc.Length != expectedStreams)
            return SevenZipArchiveDecodeResult.InvalidData;

          if (def[s])
          {
            hasExpectedCrc = true;
            expectedCrc = crc[s];
          }
        }

        // 2) Fallback: CRC на уровне folder (UnpackInfo.kCRC), только для 1-stream folder
        if (!hasExpectedCrc && expectedStreams == 1 && folderCrcDefined?[folderIndex] == true)
        {
          if (folderCrc is null)
            return SevenZipArchiveDecodeResult.InvalidData;

          hasExpectedCrc = true;
          expectedCrc = folderCrc[folderIndex];
        }

        bool hasFileCrc = fileCrcDefined?[fileIndex] == true;
        uint expectedFileCrc = hasFileCrc ? fileCrc![fileIndex] : 0;

        if (hasExpectedCrc || hasFileCrc)
        {
          ReadOnlySpan<byte> span = folderUnpacked.AsSpan(cursor, size);
          uint? actualCrc = null;

          actualCrc ??= Crc32.Compute(span);

          // (2) Новая проверка FilesInfo.kCRC:
          if (fileCrcDefined?[fileIndex] == true)
          {
            actualCrc ??= Crc32.Compute(span);
            if (actualCrc.Value != fileCrc![fileIndex])
              return SevenZipArchiveDecodeResult.InvalidData;
          }
        }

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

    while (emptyStreams is not null && fileIndex < fileCount && emptyStreams[fileIndex])
    {
      if (fileCrcDefined?[fileIndex] == true && fileCrc![fileIndex] != emptyStreamCrc)
        return SevenZipArchiveDecodeResult.InvalidData;

      decoded.Add(new SevenZipDecodedFile(names[fileIndex], []));
      fileIndex++;
    }

    if (fileIndex != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    files = [.. decoded];
    return SevenZipArchiveDecodeResult.Ok;
  }

  public static SevenZipArchiveDecodeResult DecodeToEntries(ReadOnlySpan<byte> archive, out SevenZipDecodedEntry[] entries)
    => DecodeToEntries(archive, out entries, out _);

  public static SevenZipArchiveDecodeResult DecodeToEntries(
    ReadOnlySpan<byte> archive,
    out SevenZipDecodedEntry[] entries,
    out int bytesConsumed)
  {
    entries = [];

    // 1) Сначала делаем обычную распаковку (чтобы не трогать уже стабилизированный код).
    SevenZipArchiveDecodeResult r = DecodeToArray(archive, out SevenZipDecodedFile[] files, out bytesConsumed);
    if (r != SevenZipArchiveDecodeResult.Ok)
      return r;

    // 2) Повторно читаем только header, чтобы получить EmptyStream/EmptyFile и вычислить IsDirectory.
    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult read = reader.Read(archive, out _);

    if (read == SevenZipArchiveReadResult.NeedMoreInput)
      return SevenZipArchiveDecodeResult.NeedMoreData;
    if (read == SevenZipArchiveReadResult.InvalidData)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (read == SevenZipArchiveReadResult.NotSupported)
      return SevenZipArchiveDecodeResult.NotSupported;
    if (read != SevenZipArchiveReadResult.Ok)
      return SevenZipArchiveDecodeResult.InternalError;

    SevenZipHeader? header = reader.Header;
    if (!header.HasValue)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipFilesInfo fi = header.Value.FilesInfo;

    if (fi.FileCount > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int fileCount = (int)fi.FileCount;
    if (files.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? emptyStreams = fi.EmptyStreams;
    bool[]? emptyFiles = fi.EmptyFiles;

    var result = new SevenZipDecodedEntry[fileCount];

    for (int i = 0; i < fileCount; i++)
    {
      // Если kEmptyFile отсутствует, то все EmptyStream считаем директориями.
      bool isDirectory = emptyStreams?[i] == true && emptyFiles?[i] != true;
      result[i] = new SevenZipDecodedEntry(files[i].Name, files[i].Bytes, isDirectory);
    }

    entries = result;
    return SevenZipArchiveDecodeResult.Ok;
  }

  public static SevenZipArchiveDecodeResult ExtractToDirectory(
  ReadOnlySpan<byte> archive,
  string destinationDirectory,
  bool overwrite = false)
  => ExtractToDirectory(archive, destinationDirectory, overwrite, out _);

  public static SevenZipArchiveDecodeResult ExtractToDirectory(
    ReadOnlySpan<byte> archive,
    string destinationDirectory,
    bool overwrite,
    out int bytesConsumed)
  {
    bytesConsumed = 0;

    if (destinationDirectory is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipArchiveDecodeResult r = DecodeToEntries(archive, out SevenZipDecodedEntry[] entries, out bytesConsumed);
    if (r != SevenZipArchiveDecodeResult.Ok)
      return r;

    // Читаем header ещё раз, чтобы получить метаданные (MTime / WinAttrib).
    SevenZipFilesInfo filesInfo;
    {
      SevenZipArchiveReader reader = new();
      SevenZipArchiveReadResult read = reader.Read(archive, out _);

      if (read == SevenZipArchiveReadResult.NeedMoreInput)
        return SevenZipArchiveDecodeResult.NeedMoreData;
      if (read == SevenZipArchiveReadResult.InvalidData)
        return SevenZipArchiveDecodeResult.InvalidData;
      if (read == SevenZipArchiveReadResult.NotSupported)
        return SevenZipArchiveDecodeResult.NotSupported;
      if (read != SevenZipArchiveReadResult.Ok)
        return SevenZipArchiveDecodeResult.InternalError;

      SevenZipHeader? header = reader.Header;
      if (!header.HasValue)
        return SevenZipArchiveDecodeResult.InvalidData;

      filesInfo = header.Value.FilesInfo;
    }

    int fileCount = entries.Length;
    if (filesInfo.FileCount != (ulong)fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? mTimeDefined = filesInfo.MTimeDefined;
    ulong[]? mTime = filesInfo.MTime;
    if (mTimeDefined is null != mTime is null)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (mTimeDefined is not null && mTimeDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (mTime is not null && mTime.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? winAttribDefined = filesInfo.WinAttribDefined;
    uint[]? winAttrib = filesInfo.WinAttrib;
    if (winAttribDefined is null != winAttrib is null)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (winAttribDefined is not null && winAttribDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (winAttrib is not null && winAttrib.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    try
    {
      string root = Path.GetFullPath(destinationDirectory);

      // Нормализуем так, чтобы проверка StartsWith была корректной (root обязательно с разделителем).
      string rootWithSep = root;
      if (!rootWithSep.EndsWith(Path.DirectorySeparatorChar))
        rootWithSep += Path.DirectorySeparatorChar;

      Directory.CreateDirectory(root);

      StringComparison cmp = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

      string[] fullPaths = new string[fileCount];

      for (int i = 0; i < entries.Length; i++)
      {
        if (!TryBuildSafePath(rootWithSep, entries[i].Name, cmp, out string fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        fullPaths[i] = fullPath;

        if (entries[i].IsDirectory)
        {
          Directory.CreateDirectory(fullPath);
          continue;
        }

        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is null)
          return SevenZipArchiveDecodeResult.InvalidData;

        Directory.CreateDirectory(dir);

        if (!overwrite && File.Exists(fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        File.WriteAllBytes(fullPath, entries[i].Bytes);
      }

      for (int i = 0; i < fileCount; i++)
      {
        string fullPath = fullPaths[i];
        if (fullPath.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        // kMTime: значение хранится как Windows FILETIME (UTC).
        if (mTimeDefined?[i] == true)
        {
          ulong raw = mTime![i];

          // Битые значения — InvalidData.
          if (raw > long.MaxValue)
            return SevenZipArchiveDecodeResult.InvalidData;

          DateTime dt;
          try
          {
            dt = DateTime.FromFileTimeUtc((long)raw);
          }
          catch (ArgumentOutOfRangeException)
          {
            return SevenZipArchiveDecodeResult.InvalidData;
          }

          // Best-effort: если ОС/ФС не дала выставить — не валим извлечение.
          try
          {
            if (entries[i].IsDirectory)
              Directory.SetLastWriteTimeUtc(fullPath, dt);
            else
              File.SetLastWriteTimeUtc(fullPath, dt);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }

        // kWinAttributes: применяем только на Windows.
        if (OperatingSystem.IsWindows() && winAttribDefined?[i] == true)
        {
          FileAttributes attrs = (FileAttributes)winAttrib![i];

          // Подстрахуемся по признаку IsDirectory.
          if (entries[i].IsDirectory)
            attrs |= FileAttributes.Directory;
          else
            attrs &= ~FileAttributes.Directory;

          try
          {
            File.SetAttributes(fullPath, attrs);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }
      }

      return SevenZipArchiveDecodeResult.Ok;
    }
    catch (IOException)
    {
      return SevenZipArchiveDecodeResult.InternalError;
    }
    catch (UnauthorizedAccessException)
    {
      return SevenZipArchiveDecodeResult.InternalError;
    }
    catch (ArgumentException)
    {
      return SevenZipArchiveDecodeResult.InvalidData;
    }
    catch (NotSupportedException)
    {
      return SevenZipArchiveDecodeResult.InvalidData;
    }
  }

  /// <summary>
  /// Строит безопасный путь назначения для элемента архива.
  /// Запрещает абсолютные пути, пустые сегменты, "."/"..", и выход за пределы root.
  /// </summary>
  private static bool TryBuildSafePath(
    string rootWithSep,
    string entryName,
    StringComparison comparison,
    out string fullPath)
  {
    fullPath = string.Empty;

    if (string.IsNullOrEmpty(entryName))
      return false;

    if (entryName.Contains('\0'))
      return false;

    // Для Windows дополнительно режем "C:" и альтернативные потоки.
    if (OperatingSystem.IsWindows() && entryName.Contains(':'))
      return false;

    // Нормализуем разделители на '/', чтобы проще валидировать сегменты.
    string n = entryName.Replace('\\', '/').Trim();

    // Абсолютные пути не принимаем.
    if (n.StartsWith('/'))
      return false;

    // Убираем хвостовые '/', чтобы "dir/" и "dir" были эквивалентны.
    n = n.TrimEnd('/');

    if (n.Length == 0)
      return false;

    // Валидируем сегменты: запрещаем пустые, "." и ".."
    int segStart = 0;
    for (int i = 0; i <= n.Length; i++)
      if (i == n.Length || n[i] == '/')
      {
        int segLen = i - segStart;
        if (segLen <= 0)
          return false;

        // "." ?
        if (segLen == 1 && n[segStart] == '.')
          return false;

        // ".." ?
        if (segLen == 2 && n[segStart] == '.' && n[segStart + 1] == '.')
          return false;

        segStart = i + 1;
      }

    // Конвертируем в системные разделители.
    string relative = n.Replace('/', Path.DirectorySeparatorChar);

    string combined = Path.GetFullPath(Path.Combine(rootWithSep, relative));

    // Защита от выхода за пределы root.
    if (!combined.StartsWith(rootWithSep, comparison))
      return false;

    fullPath = combined;
    return true;
  }

  private static SevenZipArchiveDecodeResult TryGetFolderFinalOutSize(
    SevenZipFolder folder,
    ulong[] folderUnpackSizes,
    out ulong finalSize)
  {
    finalSize = 0;

    if (folder.NumOutStreams > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int totalOut = (int)folder.NumOutStreams;

    if (folderUnpackSizes.Length != totalOut)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[] outUsed = new bool[totalOut];

    for (int i = 0; i < folder.BindPairs.Length; i++)
    {
      ulong outU64 = folder.BindPairs[i].OutIndex;
      if (outU64 > int.MaxValue)
        return SevenZipArchiveDecodeResult.NotSupported;

      int outIndex = (int)outU64;
      if ((uint)outIndex >= (uint)totalOut)
        return SevenZipArchiveDecodeResult.InvalidData;

      outUsed[outIndex] = true;
    }

    int finalOutIndex = -1;
    for (int i = 0; i < totalOut; i++)
    {
      if (!outUsed[i])
      {
        if (finalOutIndex != -1)
          return SevenZipArchiveDecodeResult.NotSupported; // несколько финальных выходов — не наш этап

        finalOutIndex = i;
      }
    }

    if (finalOutIndex < 0)
      return SevenZipArchiveDecodeResult.InvalidData;

    finalSize = folderUnpackSizes[finalOutIndex];
    return SevenZipArchiveDecodeResult.Ok;
  }
}
