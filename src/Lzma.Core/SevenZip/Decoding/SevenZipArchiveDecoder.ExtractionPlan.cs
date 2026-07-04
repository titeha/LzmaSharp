namespace Lzma.Core.SevenZip;

// План извлечения: структурная раскладка архива (порядок файлов, вид каждого, для файлов
// с данными — folder + размер + ожидаемый CRC), полученная из header БЕЗ декодирования данных.
// Нужен потоковому ExtractToDirectory, чтобы заранее провалидировать пути и знать, куда писать
// выход каждого folder-а, не держа весь архив в памяти. Зеркалит структурную часть DecodeToArray.
public static partial class SevenZipArchiveDecoder
{
  internal enum ExtractEntryKind
  {
    Directory,
    EmptyFile,
    DataFile,
  }

  // Одна запись плана. Для DataFile заданы FolderIndex/Size/(Has)ExpectedCrc; иначе они нейтральны.
  internal readonly record struct ExtractPlanEntry(
      string Name,
      ExtractEntryKind Kind,
      int FolderIndex,
      long Size,
      bool HasCrc,
      uint ExpectedCrc);

  // Строит план из header без декодирования. Возвращает per-file записи в порядке файлов;
  // DataFile-записи одного folder-а идут подряд (в порядке substream-ов). Ошибки — как в DecodeToArray.
  internal static SevenZipArchiveDecodeResult TryBuildExtractionPlan(
      in SevenZipHeader header,
      out ExtractPlanEntry[] plan,
      out int folderCount)
  {
    plan = [];
    folderCount = 0;

    SevenZipFilesInfo filesInfo = header.FilesInfo;

    if (filesInfo.FileCount > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;

    int fileCount = (int)filesInfo.FileCount;

    string[] names;
    if (!filesInfo.HasNames)
    {
      names = new string[fileCount];
      for (int i = 0; i < fileCount; i++)
        names[i] = $"file_{i}";
    }
    else
    {
      if (filesInfo.Names is null || filesInfo.Names.Length != fileCount)
        return SevenZipArchiveDecodeResult.InvalidData;

      names = filesInfo.Names;
    }

    bool[]? emptyStreams = filesInfo.EmptyStreams;
    if (emptyStreams is not null && emptyStreams.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[]? emptyFiles = filesInfo.EmptyFiles;

    bool[]? anti = filesInfo.Anti;
    if (anti is not null && anti.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    if (anti is not null)
    {
      for (int i = 0; i < fileCount; i++)
      {
        if (!anti[i])
          continue;

        if (emptyStreams?[i] != true)
          return SevenZipArchiveDecodeResult.InvalidData;

        return SevenZipArchiveDecodeResult.NotSupported; // anti (удаление) не поддерживаем
      }
    }

    bool[]? fileCrcDefined = filesInfo.CrcDefined;
    uint[]? fileCrc = filesInfo.Crc;

    if ((fileCrcDefined is null) != (fileCrc is null))
      return SevenZipArchiveDecodeResult.InvalidData;
    if (fileCrcDefined is not null && fileCrcDefined.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (fileCrc is not null && fileCrc.Length != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    var result = new ExtractPlanEntry[fileCount];

    // Запись пустого потока (kEmptyStream): директория, если не помечена kEmptyFile, иначе пустой файл.
    static ExtractPlanEntry EmptyEntry(string name, bool[]? emptyFiles, int i)
    {
      bool isDirectory = emptyFiles?[i] != true;
      return new ExtractPlanEntry(
          name,
          isDirectory ? ExtractEntryKind.Directory : ExtractEntryKind.EmptyFile,
          FolderIndex: -1, Size: 0, HasCrc: false, ExpectedCrc: 0);
    }

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
      for (int i = 0; i < fileCount; i++)
        result[i] = EmptyEntry(names[i], emptyFiles, i);

      plan = result;
      folderCount = 0;
      return SevenZipArchiveDecodeResult.Ok;
    }

    SevenZipStreamsInfo? streamsInfo = header.StreamsInfo;
    if (streamsInfo is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipUnpackInfo? unpackInfo = streamsInfo.UnpackInfo;
    if (streamsInfo.PackInfo is null || unpackInfo is null)
      return SevenZipArchiveDecodeResult.InvalidData;

    int fCount = unpackInfo.Folders.Length;
    if (fCount <= 0)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipSubStreamsInfo? sub = streamsInfo.SubStreamsInfo;

    ulong[] numUnpackStreamsPerFolder;
    ulong[][] unpackSizesPerFolder;

    if (sub is not null)
    {
      numUnpackStreamsPerFolder = sub.NumUnpackStreamsPerFolder;
      unpackSizesPerFolder = sub.UnpackSizesPerFolder;

      if (numUnpackStreamsPerFolder.Length != fCount)
        return SevenZipArchiveDecodeResult.InvalidData;
      if (unpackSizesPerFolder.Length != fCount)
        return SevenZipArchiveDecodeResult.InvalidData;
    }
    else
    {
      numUnpackStreamsPerFolder = new ulong[fCount];
      unpackSizesPerFolder = new ulong[fCount][];

      if (unpackInfo.FolderUnpackSizes.Length != fCount)
        return SevenZipArchiveDecodeResult.InvalidData;

      for (int i = 0; i < fCount; i++)
      {
        numUnpackStreamsPerFolder[i] = 1;

        ulong[] folderSizes = unpackInfo.FolderUnpackSizes[i];
        if (folderSizes is null || folderSizes.Length == 0)
          return SevenZipArchiveDecodeResult.InvalidData;

        SevenZipArchiveDecodeResult sizeRes = TryGetFolderFinalOutSize(
            unpackInfo.Folders[i], folderSizes, out ulong finalSize);
        if (sizeRes != SevenZipArchiveDecodeResult.Ok)
          return sizeRes;

        unpackSizesPerFolder[i] = [finalSize];
      }
    }

    bool[]? folderCrcDefined = unpackInfo.FolderCrcDefined;
    uint[]? folderCrc = unpackInfo.FolderCrc;

    if ((folderCrcDefined is null) != (folderCrc is null))
      return SevenZipArchiveDecodeResult.InvalidData;
    if (folderCrcDefined is not null && folderCrcDefined.Length != fCount)
      return SevenZipArchiveDecodeResult.InvalidData;
    if (folderCrc is not null && folderCrc.Length != fCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    bool[][]? unpackCrcDefinedPerFolder = sub?.UnpackCrcDefinedPerFolder;
    uint[][]? unpackCrcPerFolder = sub?.UnpackCrcPerFolder;

    if ((unpackCrcDefinedPerFolder is null) != (unpackCrcPerFolder is null))
      return SevenZipArchiveDecodeResult.InvalidData;
    if (unpackCrcDefinedPerFolder is not null && unpackCrcDefinedPerFolder.Length != fCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    ulong totalUnpackStreamsU64 = 0;
    for (int i = 0; i < fCount; i++)
      totalUnpackStreamsU64 += numUnpackStreamsPerFolder[i];

    if (totalUnpackStreamsU64 > int.MaxValue)
      return SevenZipArchiveDecodeResult.NotSupported;
    if ((int)totalUnpackStreamsU64 != nonEmptyFilesCount)
      return SevenZipArchiveDecodeResult.NotSupported;

    int fileIndex = 0;

    for (int folder = 0; folder < fCount; folder++)
    {
      ulong expectedStreamsU64 = numUnpackStreamsPerFolder[folder];
      if (expectedStreamsU64 > int.MaxValue)
        return SevenZipArchiveDecodeResult.NotSupported;
      int expectedStreams = (int)expectedStreamsU64;

      ulong[] sizes = unpackSizesPerFolder[folder];
      if (sizes is null || sizes.Length != expectedStreams)
        return SevenZipArchiveDecodeResult.InvalidData;

      for (int s = 0; s < expectedStreams; s++)
      {
        // Пропускаем файлы без потока (kEmptyStream) — директории / пустые файлы.
        while (emptyStreams is not null && fileIndex < fileCount && emptyStreams[fileIndex])
        {
          result[fileIndex] = EmptyEntry(names[fileIndex], emptyFiles, fileIndex);
          fileIndex++;
        }

        if (fileIndex >= fileCount)
          return SevenZipArchiveDecodeResult.InvalidData;

        ulong sizeU64 = sizes[s];
        if (sizeU64 > long.MaxValue)
          return SevenZipArchiveDecodeResult.NotSupported;
        long size = (long)sizeU64;

        // Ожидаемый CRC: сначала stream-level (SubStreamsInfo.kCRC), затем folder-level (для 1-stream),
        // затем file-level (FilesInfo.kCRC). Все относятся к одному содержимому — достаточно одного.
        bool hasCrc = false;
        uint expectedCrc = 0;

        if (unpackCrcDefinedPerFolder is not null)
        {
          bool[] def = unpackCrcDefinedPerFolder[folder];
          uint[] crc = unpackCrcPerFolder![folder];

          if (def is null || crc is null || def.Length != expectedStreams || crc.Length != expectedStreams)
            return SevenZipArchiveDecodeResult.InvalidData;

          if (def[s])
          {
            hasCrc = true;
            expectedCrc = crc[s];
          }
        }

        if (!hasCrc && expectedStreams == 1 && folderCrcDefined?[folder] == true)
        {
          if (folderCrc is null)
            return SevenZipArchiveDecodeResult.InvalidData;

          hasCrc = true;
          expectedCrc = folderCrc[folder];
        }

        if (!hasCrc && fileCrcDefined?[fileIndex] == true)
        {
          hasCrc = true;
          expectedCrc = fileCrc![fileIndex];
        }

        result[fileIndex] = new ExtractPlanEntry(
            names[fileIndex], ExtractEntryKind.DataFile, folder, size, hasCrc, expectedCrc);
        fileIndex++;
      }
    }

    // Хвостовые файлы без потока.
    while (emptyStreams is not null && fileIndex < fileCount && emptyStreams[fileIndex])
    {
      result[fileIndex] = EmptyEntry(names[fileIndex], emptyFiles, fileIndex);
      fileIndex++;
    }

    if (fileIndex != fileCount)
      return SevenZipArchiveDecodeResult.InvalidData;

    plan = result;
    folderCount = fCount;
    return SevenZipArchiveDecodeResult.Ok;
  }
}
