using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Декодирует 7z-архив в памяти и возвращает распакованные файлы или записи архива.
/// </summary>
/// <remarks>
/// Поддерживаемый decoder-path постепенно расширяется и сейчас включает:
/// <list type="bullet">
/// <item>
/// <description>обычные незашифрованные folder-ы с линейным конвейером coder-ов;</description>
/// </item>
/// <item>
/// <description>часть multi-stream сценариев, включая специальную ветку BCJ2;</description>
/// </item>
/// <item>
/// <description>AES-256-CBC для файловых потоков и зашифрованного заголовка;</description>
/// </item>
/// <item>
/// <description>
/// экспериментальные private GOST coder-ы LzmaSharp для decoder-path.
/// </description>
/// </item>
/// </list>
/// Реализация остаётся decoder-only: writer 7z-архивов здесь не реализован.
/// </remarks>
/// <remarks>
/// Класс разбит на partial-файлы: основной decode/extract — здесь, хелперы безопасной
/// записи на диск (валидация путей, device-имена, конфликты) — в
/// <c>SevenZipArchiveDecoder.SafeExtraction.cs</c>.
/// </remarks>
public static partial class SevenZipArchiveDecoder
{
  /// <summary>
  /// Декодирует 7z-архив, содержащий ровно один файл.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(
      ReadOnlySpan<byte> archiveBytes,
      out byte[] fileBytes,
      out string fileName)
  {
    return DecodeSingleFileToArray(
        archiveBytes: archiveBytes,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out fileBytes,
        fileName: out fileName,
        bytesConsumed: out _);
  }

  /// <summary>
  /// Декодирует 7z-архив, содержащий ровно один файл.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(
      ReadOnlySpan<byte> archiveBytes,
      SevenZipDecodeOptions options,
      out byte[] fileBytes,
      out string fileName)
  {
    return DecodeSingleFileToArray(
        archiveBytes: archiveBytes,
        options: options,
        fileBytes: out fileBytes,
        fileName: out fileName,
        bytesConsumed: out _);
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
    return DecodeSingleFileToArray(
        archiveBytes: archiveBytes,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out fileBytes,
        fileName: out fileName,
        bytesConsumed: out bytesConsumed);
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
      SevenZipDecodeOptions options,
      out byte[] fileBytes,
      out string fileName,
      out int bytesConsumed)
  {
    ArgumentNullException.ThrowIfNull(options);

    SevenZipArchiveDecodeResult r = DecodeToEntries(
        archive: archiveBytes,
        options: options,
        entries: out SevenZipDecodedEntry[] decodedEntries,
        bytesConsumed: out bytesConsumed);

    if (r != SevenZipArchiveDecodeResult.Ok)
    {
      fileBytes = [];
      fileName = string.Empty;
      return r;
    }

    if (decodedEntries.Length != 1 || decodedEntries[0].IsDirectory)
    {
      fileBytes = [];
      fileName = string.Empty;
      return SevenZipArchiveDecodeResult.NotSupported;
    }

    fileBytes = decodedEntries[0].Bytes;
    fileName = decodedEntries[0].Name;
    return SevenZipArchiveDecodeResult.Ok;
  }

  /// <summary>
  /// Декодирует 7z-архив и возвращает все файлы.
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeAllFilesToArray(ReadOnlySpan<byte> archiveBytes, out SevenZipDecodedFile[] files)
    => DecodeToArray(archiveBytes, out files);

  public static SevenZipArchiveDecodeResult DecodeToArray(
    ReadOnlySpan<byte> archive,
    out SevenZipDecodedFile[] files) => DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out files,
        bytesConsumed: out _);

  public static SevenZipArchiveDecodeResult DecodeToArray(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      out SevenZipDecodedFile[] files) => DecodeToArray(
        archive: archive,
        options: options,
        files: out files,
        bytesConsumed: out _);

  public static SevenZipArchiveDecodeResult DecodeToArray(
      ReadOnlySpan<byte> archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed) => DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out files,
        bytesConsumed: out bytesConsumed);

  // Лёгкий синхронный адаптер IProgress из делегата. В отличие от System.Progress<T>
  // (постит отчёты асинхронно через SynchronizationContext) — вызывает делегат на месте,
  // что нужно для перевода folder-local отчётов в глобальные внутри цикла декодирования.
  private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }

  public static SevenZipArchiveDecodeResult DecodeToArray(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(options);

    files = [];
    bytesConsumed = 0;

    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult read = reader.Read(input: archive, options: options, bytesConsumed: out bytesConsumed);

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

    // kAnti: элементы "удаления" из update-архивов пока не поддерживаются.
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

    // PackInfo.kCRC: CRC32 по packed stream'ам (если задано).
    if (streamsInfo.PackInfo is not { } packInfo)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? packCrcDefined = packInfo.CrcDefined;
    uint[]? packCrc = packInfo.Crc;

    if ((packCrcDefined is null) != (packCrc is null))
      return SevenZipArchiveDecodeResult.InvalidData;

    if (packCrcDefined is not null)
    {
      if (packCrcDefined.Length != packInfo.PackSizes.Length)
        return SevenZipArchiveDecodeResult.InvalidData;

      if (packCrc!.Length != packInfo.PackSizes.Length)
        return SevenZipArchiveDecodeResult.InvalidData;

      ulong start = packInfo.PackPos;

      for (int i = 0; i < packInfo.PackSizes.Length; i++)
      {
        ulong sizeU64 = packInfo.PackSizes[i];

        if (start > (ulong)packed.Length)
          return SevenZipArchiveDecodeResult.InvalidData;

        if (sizeU64 > (ulong)packed.Length - start)
          return SevenZipArchiveDecodeResult.InvalidData;

        if (packCrcDefined[i])
        {
          if (start > int.MaxValue || sizeU64 > int.MaxValue)
            return SevenZipArchiveDecodeResult.NotSupported;

          uint actual = Crc32.Compute(packed.Slice((int)start, (int)sizeU64));
          if (actual != packCrc[i])
            return SevenZipArchiveDecodeResult.InvalidData;
        }

        start += sizeU64;
      }
    }

    List<SevenZipDecodedFile> decoded = new(fileCount);

    int fileIndex = 0;

    // Суммарный размер распаковки (для отчёта прогресса). Считаем заранее по метаданным.
    long totalUnpackedBytes = 0;
    if (progress is not null)
    {
      for (int f = 0; f < folderCount; f++)
      {
        ulong[]? folderSizes = unpackSizesPerFolder[f];
        if (folderSizes is not null)
          for (int s = 0; s < folderSizes.Length; s++)
            totalUnpackedBytes += (long)folderSizes[s];
      }

      progress.Report(new SevenZipProgress(0, totalUnpackedBytes));
    }

    long processedUnpackedBytes = 0;

    for (int folderIndex = 0; folderIndex < folderCount; folderIndex++)
    {
      token.ThrowIfCancellationRequested(); // кооперативная отмена между folder-ами

      // Within-folder прогресс: транслируем folder-local BytesWritten (накопленный выход
      // текущего folder) в глобальный отчёт = (обработано в предыдущих folder-ах + local).
      // Clamp на total — защита от возможного перелёта (напр. промежуточные размеры в цепочке).
      long processedBefore = processedUnpackedBytes;
      IProgress<LzmaProgress>? folderProgress = progress is null ? null
          : new DelegateProgress<LzmaProgress>(lp => progress.Report(new SevenZipProgress(
              Math.Min(processedBefore + lp.BytesWritten, totalUnpackedBytes), totalUnpackedBytes)));

      SevenZipFolderDecodeResult folderRes = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packed,
        folderIndex: folderIndex,
        options: options,
        output: out byte[] folderUnpacked,
        progress: folderProgress);

      if (folderRes == SevenZipFolderDecodeResult.InvalidData)
        return SevenZipArchiveDecodeResult.InvalidData;
      if (folderRes == SevenZipFolderDecodeResult.NotSupported)
        return SevenZipArchiveDecodeResult.NotSupported;
      if (folderRes != SevenZipFolderDecodeResult.Ok)
        return SevenZipArchiveDecodeResult.InternalError;

      if (progress is not null)
      {
        processedUnpackedBytes += folderUnpacked.Length;
        progress.Report(new SevenZipProgress(processedUnpackedBytes, totalUnpackedBytes));
      }

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
          uint actualCrc = Crc32.Compute(span);

          // 1) CRC на уровне unpack-stream (SubStreamsInfo.kCRC) / fallback на folder CRC.
          if (hasExpectedCrc && actualCrc != expectedCrc)
            return SevenZipArchiveDecodeResult.InvalidData;

          // 2) CRC на уровне файла (FilesInfo.kCRC).
          if (hasFileCrc && actualCrc != expectedFileCrc)
            return SevenZipArchiveDecodeResult.InvalidData;
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

  public static SevenZipArchiveDecodeResult DecodeToEntries(
    ReadOnlySpan<byte> archive,
    out SevenZipDecodedEntry[] entries)
  {
    return DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out entries,
        bytesConsumed: out _);
  }

  public static SevenZipArchiveDecodeResult DecodeToEntries(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      out SevenZipDecodedEntry[] entries)
  {
    return DecodeToEntries(
        archive: archive,
        options: options,
        entries: out entries,
        bytesConsumed: out _);
  }

  public static SevenZipArchiveDecodeResult DecodeToEntries(
      ReadOnlySpan<byte> archive,
      out SevenZipDecodedEntry[] entries,
      out int bytesConsumed)
  {
    return DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out entries,
        bytesConsumed: out bytesConsumed);
  }

  public static SevenZipArchiveDecodeResult DecodeToEntries(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      out SevenZipDecodedEntry[] entries,
      out int bytesConsumed,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(options);

    entries = [];
    bytesConsumed = 0;

    // 1) Сначала делаем обычную распаковку (чтобы не трогать уже стабилизированный код).
    SevenZipArchiveDecodeResult r = DecodeToArray(archive, options, out SevenZipDecodedFile[] files, out bytesConsumed, progress, token);
    if (r != SevenZipArchiveDecodeResult.Ok)
      return r;

    // 2) Повторно читаем только header, чтобы получить EmptyStream/EmptyFile и вычислить IsDirectory.
    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult read = reader.Read(input: archive, options: options, bytesConsumed: out _);

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
  {
    return ExtractToDirectory(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        destinationDirectory: destinationDirectory,
        overwrite: overwrite,
        bytesConsumed: out _);
  }

  public static SevenZipArchiveDecodeResult ExtractToDirectory(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      string destinationDirectory,
      bool overwrite = false)
  {
    return ExtractToDirectory(
        archive: archive,
        options: options,
        destinationDirectory: destinationDirectory,
        overwrite: overwrite,
        bytesConsumed: out _);
  }

  public static SevenZipArchiveDecodeResult ExtractToDirectory(
      ReadOnlySpan<byte> archive,
      string destinationDirectory,
      bool overwrite,
      out int bytesConsumed)
  {
    return ExtractToDirectory(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        destinationDirectory: destinationDirectory,
        overwrite: overwrite,
        bytesConsumed: out bytesConsumed);
  }

  public static SevenZipArchiveDecodeResult ExtractToDirectory(
      ReadOnlySpan<byte> archive,
      SevenZipDecodeOptions options,
      string destinationDirectory,
      bool overwrite,
      out int bytesConsumed,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(options);
    bytesConsumed = 0;

    if (destinationDirectory is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipArchiveDecodeResult r = DecodeToEntries(archive, options, out SevenZipDecodedEntry[] entries, out bytesConsumed, progress, token);
    if (r != SevenZipArchiveDecodeResult.Ok)
      return r;

    // Читаем header ещё раз, чтобы получить метаданные (MTime / WinAttrib).
    SevenZipFilesInfo filesInfo;
    {
      SevenZipArchiveReader reader = new();
      SevenZipArchiveReadResult read = reader.Read(input: archive, options: options, bytesConsumed: out _);

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

    bool[]? cTimeDefined = filesInfo.CTimeDefined;
    ulong[]? cTime = filesInfo.CTime;
    if (cTimeDefined is null != cTime is null)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (cTimeDefined is not null && cTimeDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (cTime is not null && cTime.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? aTimeDefined = filesInfo.ATimeDefined;
    ulong[]? aTime = filesInfo.ATime;
    if (aTimeDefined is null != aTime is null)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (aTimeDefined is not null && aTimeDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (aTime is not null && aTime.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    // Валидируем FILETIME до начала записи на диск, чтобы не оставлять
    // частично извлечённые файлы при битых metadata.
    for (int i = 0; i < fileCount; i++)
    {
      if (cTimeDefined?[i] == true && !IsValidFileTime(cTime![i]))
        return SevenZipArchiveDecodeResult.InvalidData;

      if (aTimeDefined?[i] == true && !IsValidFileTime(aTime![i]))
        return SevenZipArchiveDecodeResult.InvalidData;

      if (mTimeDefined?[i] == true && !IsValidFileTime(mTime![i]))
        return SevenZipArchiveDecodeResult.InvalidData;
    }

    try
    {
      string root = Path.GetFullPath(destinationDirectory);

      StringComparison cmp = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

      // destinationDirectory должен быть каталогом, а не существующим файлом.
      if (File.Exists(root))
        return SevenZipArchiveDecodeResult.InvalidData;

      // destinationDirectory должен быть каталогом.
      // Если сам путь или любой его родительский сегмент уже существует как файл,
      // считаем это ошибкой входных данных, а не InternalError.
      if (HasFileOnDirectoryPath(root, cmp))
        return SevenZipArchiveDecodeResult.InvalidData;

      // Нормализуем так, чтобы проверка StartsWith была корректной (root обязательно с разделителем).
      string rootWithSep = root;
      if (!rootWithSep.EndsWith(Path.DirectorySeparatorChar))
        rootWithSep += Path.DirectorySeparatorChar;

      Directory.CreateDirectory(root);

      StringComparer pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

      string[] fullPaths = new string[fileCount];

      HashSet<string> seenOutputPaths = new(pathComparer);

      // Сначала считаем и валидируем ВСЕ итоговые пути.
      // Это нужно, чтобы не получить частичное извлечение,
      // если две записи архива схлопываются в один путь на текущей ОС
      // (например, A.bin и a.bin на Windows).
      for (int i = 0; i < entries.Length; i++)
      {
        if (!TryBuildSafePath(rootWithSep, entries[i].Name, cmp, out string fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        if (!seenOutputPaths.Add(fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        fullPaths[i] = fullPath;
      }

      HashSet<string> plannedFilePaths = new(pathComparer);
      for (int i = 0; i < entries.Length; i++)
      {
        if (!entries[i].IsDirectory)
          plannedFilePaths.Add(fullPaths[i]);
      }

      // Если один entry архива является файлом, а другой лежит "под ним"
      // (например, "dir" и "dir/file.bin"), отказываем ДО начала записи.
      for (int i = 0; i < entries.Length; i++)
      {
        string? current = Path.GetDirectoryName(fullPaths[i]);

        while (current is not null)
        {
          if (string.Equals(current, root, cmp))
            break;

          if (plannedFilePaths.Contains(current))
            return SevenZipArchiveDecodeResult.InvalidData;

          string? parent = Path.GetDirectoryName(current);
          if (parent is null || string.Equals(parent, current, cmp))
            break;

          current = parent;
        }
      }

      // Дополнительно заранее валидируем конфликты с уже существующей ФС,
      // чтобы не получить частичное извлечение после записи ранних файлов.
      for (int i = 0; i < entries.Length; i++)
      {
        string fullPath = fullPaths[i];
        if (fullPath.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        if (entries[i].IsDirectory)
        {
          // Каталог нельзя создавать, если он сам
          // или любой его родитель уже существует как файл.
          if (HasFileOnPath(root, fullPath, includeSelf: true, cmp))
            return SevenZipArchiveDecodeResult.InvalidData;

          continue;
        }

        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is null)
          return SevenZipArchiveDecodeResult.InvalidData;

        // Родительские каталоги файла не должны конфликтовать с существующими файлами.
        if (HasFileOnPath(root, fullPath, includeSelf: false, cmp))
          return SevenZipArchiveDecodeResult.InvalidData;

        // Если на месте файла уже есть каталог — это конфликт.
        if (Directory.Exists(fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        // Если файл уже существует и overwrite=false — это тоже конфликт.
        if (File.Exists(fullPath) && !overwrite)
          return SevenZipArchiveDecodeResult.InvalidData;
      }

      // Только после этого реально создаём каталоги и пишем файлы.
      for (int i = 0; i < entries.Length; i++)
      {
        string fullPath = fullPaths[i];
        if (fullPath.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        if (entries[i].IsDirectory)
        {
          // Нельзя создавать каталог, если он сам или любой его родитель уже существует как файл.
          if (HasFileOnPath(root, fullPath, includeSelf: true, cmp))
            return SevenZipArchiveDecodeResult.InvalidData;

          Directory.CreateDirectory(fullPath);
          continue;
        }

        string? dir = Path.GetDirectoryName(fullPath);
        if (dir is null)
          return SevenZipArchiveDecodeResult.InvalidData;

        // Нельзя создавать родительские каталоги, если на этом пути уже лежит файл.
        if (HasFileOnPath(root, fullPath, includeSelf: false, cmp))
          return SevenZipArchiveDecodeResult.InvalidData;

        Directory.CreateDirectory(dir);

        if (Directory.Exists(fullPath))
          return SevenZipArchiveDecodeResult.InvalidData;

        if (File.Exists(fullPath))
        {
          if (!overwrite)
            return SevenZipArchiveDecodeResult.InvalidData;

          if (!TryPrepareExistingFileForOverwrite(fullPath))
            return SevenZipArchiveDecodeResult.InvalidData;
        }

        File.WriteAllBytes(fullPath, entries[i].Bytes);
      }

      for (int i = 0; i < fileCount; i++)
      {
        string fullPath = fullPaths[i];
        if (fullPath.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        // kCTime: сырой FILETIME (UTC). Best-effort: если ОС/ФС не дала — не валим извлечение.
        // Но битые значения в заголовке считаем InvalidData.
        if (cTimeDefined?[i] == true)
        {
          ulong raw = cTime![i];
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

          try
          {
            if (entries[i].IsDirectory)
              Directory.SetCreationTimeUtc(fullPath, dt);
            else
              File.SetCreationTimeUtc(fullPath, dt);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
          catch (PlatformNotSupportedException) { }
        }

        // kATime: сырой FILETIME (UTC).
        if (aTimeDefined?[i] == true)
        {
          ulong raw = aTime![i];
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

          try
          {
            if (entries[i].IsDirectory)
              Directory.SetLastAccessTimeUtc(fullPath, dt);
            else
              File.SetLastAccessTimeUtc(fullPath, dt);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
          catch (PlatformNotSupportedException) { }
        }

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
