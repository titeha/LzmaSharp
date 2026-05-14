using System.Text;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Строит 7z-архивы для поддерживаемых writer-сценариев.
/// </summary>
public static class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит минимальный пустой 7z-архив без packed stream-ов и без файлов.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildEmptyArchive(out byte[] archive)
  {
    byte[] nextHeaderBytes =
    [
        SevenZipNid.Header,
            SevenZipNid.End,
        ];

    archive = BuildArchiveWithNextHeader(nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит 7z-архив с одним пустым файлом.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildSingleEmptyFileArchive(
      string fileName,
      out byte[] archive)
  {
    archive = [];

    if (!IsSupportedEntryName(fileName))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!TryBuildSingleEmptyFileNextHeader(fileName, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithNextHeader(nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит 7z-архив для поддерживаемого набора элементов.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (entries is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(entries))
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 1)
    {
      SevenZipArchiveWriterEntry file = entries[0];

      if (file.IsDirectory)
        return BuildEmptyEntriesArchive(entries, out archive);

      if (file.Content.Length == 0)
        return BuildSingleEmptyFileArchive(file.Name, out archive);

      return BuildSingleFileCopyArchive(file.Name, file.Content, out archive);
    }

    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    if (AllEntriesAreNonEmptyFiles(entries))
      return BuildCopyFilesArchive(entries, out archive);

    if (HasNonEmptyFiles(entries))
      return BuildMixedCopyEntriesArchive(entries, out archive);

    return SevenZipArchiveWriteResult.NotSupported;
  }

  /// <summary>
  /// Проверяет, что среди entry есть хотя бы один непустой файл.
  /// </summary>
  private static bool HasNonEmptyFiles(IReadOnlyList<SevenZipArchiveWriterEntry> entries) => CountNonEmptyFiles(entries) != 0;

  /// <summary>
  /// Строит 7z-архив со смесью empty entries и непустых Copy-файлов.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildMixedCopyEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (!TryBuildCopyPackedData(
        entries,
        out byte[] packedData,
        out int[] sizes,
        out uint[] crcs))
      return SevenZipArchiveWriteResult.NotSupported;

    if (!TryBuildMixedCopyEntriesNextHeader(
            entries,
            sizes,
            crcs,
            out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Считает непустые файлы среди entry.
  /// </summary>
  private static int CountNonEmptyFiles(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    int count = 0;

    for (int i = 0; i < entries.Count; i++)
      if (IsNonEmptyFile(entries[i]))
        count++;

    return count;
  }

  /// <summary>
  /// Строит next header для смешанного архива с empty entries и непустыми Copy-файлами.
  /// </summary>
  private static bool TryBuildMixedCopyEntriesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] sizes,
      uint[] crcs,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteCopyFilesPackInfo(header, sizes, crcs))
      return false;

    if (!TryWriteCopyFilesUnpackInfo(header, sizes, crcs))
      return false;

    header.Add(SevenZipNid.End);

    if (!TryWriteMixedCopyEntriesFilesInfo(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для смешанного набора empty entries и непустых Copy-файлов.
  /// </summary>
  private static bool TryWriteMixedCopyEntriesFilesInfo(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, (ulong)entries.Count))
      return false;

    header.Add(SevenZipNid.EmptyStream);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(entries.Count)))
      return false;

    WriteEmptyStreamBitVector(header, entries);

    header.Add(SevenZipNid.EmptyFile);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(CountEmptyEntries(entries))))
      return false;

    WriteEmptyFileSubVector(header, entries);

    if (!TryWriteFileNamesProperty(header, entries))
      return false;

    if (!TryWriteMixedCopyEntriesCrcProperty(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Считает empty entries.
  /// </summary>
  private static int CountEmptyEntries(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    int count = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (entry.IsDirectory || entry.Content.Length == 0)
        count++;
    }

    return count;
  }

  /// <summary>
  /// Пишет bit-vector EmptyStream для всех entry.
  /// true означает, что у entry нет файловых данных.
  /// </summary>
  private static void WriteEmptyStreamBitVector(
      List<byte> destination,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries) => WriteBitVector(
        destination,
        entries.Count,
        index =>
        {
          SevenZipArchiveWriterEntry entry = entries[index];
          return entry.IsDirectory || entry.Content.Length == 0;
        });

  /// <summary>
  /// Пишет EmptyFile sub-vector только для empty stream entries.
  /// true означает пустой файл, false означает директорию.
  /// </summary>
  private static void WriteEmptyFileSubVector(
      List<byte> destination,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    int emptyEntryCount = CountEmptyEntries(entries);
    bool[] bits = new bool[emptyEntryCount];

    int outputIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!entry.IsDirectory && entry.Content.Length != 0)
        continue;

      bits[outputIndex] = !entry.IsDirectory;
      outputIndex++;
    }

    WriteBitVector(destination, bits.Length, index => bits[index]);
  }

  /// <summary>
  /// Пишет CRC-свойство FilesInfo для смешанного набора entry.
  /// CRC задаётся только для непустых файлов.
  /// </summary>
  private static bool TryWriteMixedCopyEntriesCrcProperty(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    bool[] defined = new bool[entries.Count];
    uint[] crcs = new uint[entries.Count];

    int definedCount = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (entry.IsDirectory || entry.Content.Length == 0)
        continue;

      defined[i] = true;
      crcs[i] = Crc32.Compute(entry.Content);
      definedCount++;
    }

    header.Add(SevenZipNid.Crc);

    ulong propertySize =
        1UL
        + (ulong)GetBitVectorByteCount(entries.Count)
        + ((ulong)definedCount * 4UL);

    if (!TryWriteUInt64(header, propertySize))
      return false;

    header.Add(0x00);
    WriteDefinedBitVector(header, defined);

    for (int i = 0; i < entries.Count; i++)
    {
      if (!defined[i])
        continue;

      WriteUInt32LittleEndian(header, crcs[i]);
    }

    return true;
  }

  /// <summary>
  /// Пишет bit-vector для массива defined-флагов.
  /// </summary>
  private static void WriteDefinedBitVector(
      List<byte> destination,
      bool[] defined) => WriteBitVector(destination, defined.Length, index => defined[index]);

  /// <summary>
  /// Проверяет, что все элементы являются непустыми файлами.
  /// </summary>
  private static bool AllEntriesAreNonEmptyFiles(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    for (int i = 0; i < entries.Count; i++)
      if (!IsNonEmptyFile(entries[i]))
        return false;

    return true;
  }

  /// <summary>
  /// Строит 7z-архив с несколькими непустыми файлами через Copy coder.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildCopyFilesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (!TryBuildCopyPackedData(
        entries,
        out byte[] packedData,
        out int[] sizes,
        out uint[] crcs))
      return SevenZipArchiveWriteResult.NotSupported;

    if (!TryBuildCopyFilesNextHeader(
            entries,
            sizes,
            crcs,
            out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит packed data, размеры и CRC для непустых Copy-файлов из набора entry.
  /// </summary>
  private static bool TryBuildCopyPackedData(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] packedData,
      out int[] sizes,
      out uint[] crcs)
  {
    packedData = [];
    sizes = [];
    crcs = [];

    int nonEmptyFileCount = CountNonEmptyFiles(entries);
    long totalLength = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      totalLength += entry.Content.Length;

      if (totalLength > int.MaxValue)
        return false;
    }

    packedData = new byte[(int)totalLength];
    sizes = new int[nonEmptyFileCount];
    crcs = new uint[nonEmptyFileCount];

    int outputOffset = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      byte[] content = entry.Content;

      sizes[streamIndex] = content.Length;
      crcs[streamIndex] = Crc32.Compute(content);

      content.CopyTo(packedData.AsSpan(outputOffset));

      outputOffset += content.Length;
      streamIndex++;
    }

    return true;
  }

  /// <summary>
  /// Проверяет, что entry является непустым файлом.
  /// </summary>
  private static bool IsNonEmptyFile(SevenZipArchiveWriterEntry entry) => !entry.IsDirectory && entry.Content.Length != 0;

  /// <summary>
  /// Строит next header для архива с несколькими непустыми файлами через Copy coder.
  /// </summary>
  private static bool TryBuildCopyFilesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] sizes,
      uint[] crcs,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteCopyFilesPackInfo(header, sizes, crcs))
      return false;

    if (!TryWriteCopyFilesUnpackInfo(header, sizes, crcs))
      return false;

    header.Add(SevenZipNid.End);

    if (!TryWriteCopyFilesFilesInfo(header, entries, crcs))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет PackInfo для нескольких packed stream-ов.
  /// </summary>
  private static bool TryWriteCopyFilesPackInfo(
      List<byte> header,
      int[] sizes,
      uint[] crcs)
  {
    header.Add(SevenZipNid.PackInfo);

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, (ulong)sizes.Length))
      return false;

    header.Add(SevenZipNid.Size);

    for (int i = 0; i < sizes.Length; i++)
    {
      if (!TryWriteUInt64(header, (ulong)sizes[i]))
        return false;
    }

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, crcs);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет UnpackInfo для нескольких folder-ов с Copy coder.
  /// </summary>
  private static bool TryWriteCopyFilesUnpackInfo(
      List<byte> header,
      int[] sizes,
      uint[] crcs)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)sizes.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < sizes.Length; i++)
    {
      if (!TryWriteUInt64(header, 1))
        return false;

      // Copy coder: Method ID = 00, id size = 1, без properties.
      header.Add(0x01);
      header.Add(0x00);
    }

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < sizes.Length; i++)
    {
      if (!TryWriteUInt64(header, (ulong)sizes[i]))
        return false;
    }

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, crcs);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для нескольких непустых Copy-файлов.
  /// </summary>
  private static bool TryWriteCopyFilesFilesInfo(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      uint[] crcs)
  {
    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, (ulong)entries.Count))
      return false;

    if (!TryWriteFileNamesProperty(header, entries))
      return false;

    if (!TryWriteDefinedCrcProperty(header, crcs))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет CRC-свойство FilesInfo для набора entry с allAreDefined.
  /// </summary>
  private static bool TryWriteDefinedCrcProperty(
      List<byte> header,
      uint[] crcs)
  {
    header.Add(SevenZipNid.Crc);

    ulong propertySize = 1UL + ((ulong)crcs.Length * 4UL);

    if (!TryWriteUInt64(header, propertySize))
      return false;

    WriteAllDefinedCrcDigests(header, crcs);

    return true;
  }

  /// <summary>
  /// Пишет CRC digest для набора stream-ов с allAreDefined.
  /// </summary>
  private static void WriteAllDefinedCrcDigests(
      List<byte> header,
      uint[] crcs)
  {
    header.Add(0x01);

    for (int i = 0; i < crcs.Length; i++)
      WriteUInt32LittleEndian(header, crcs[i]);
  }

  /// <summary>
  /// Строит 7z-архив с одним непустым файлом через Copy coder.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildSingleFileCopyArchive(
      string fileName,
      byte[] content,
      out byte[] archive)
  {
    archive = [];

    if (!IsSupportedEntryName(fileName))
      return SevenZipArchiveWriteResult.InvalidData;

    if (content is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (content.Length == 0)
      return BuildSingleEmptyFileArchive(fileName, out archive);

    uint contentCrc = Crc32.Compute(content);

    if (!TryBuildSingleFileCopyNextHeader(
            fileName,
            content.Length,
            contentCrc,
            out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(content, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит next header для архива с одним непустым файлом через Copy coder.
  /// </summary>
  private static bool TryBuildSingleFileCopyNextHeader(
      string fileName,
      int contentLength,
      uint contentCrc,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(128)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteSinglePackInfo(header, contentLength, contentCrc))
      return false;

    if (!TryWriteSingleCopyUnpackInfo(header, contentLength, contentCrc))
      return false;

    header.Add(SevenZipNid.End);

    if (!TryWriteSingleFileCopyFilesInfo(header, fileName, contentCrc))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет PackInfo для одного packed stream-а.
  /// </summary>
  private static bool TryWriteSinglePackInfo(
      List<byte> header,
      int packedSize,
      uint packedCrc)
  {
    header.Add(SevenZipNid.PackInfo);

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(SevenZipNid.Size);

    if (!TryWriteUInt64(header, (ulong)packedSize))
      return false;

    header.Add(SevenZipNid.Crc);
    WriteSingleDefinedStreamCrcDigest(header, packedCrc);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет UnpackInfo для одного folder-а с Copy coder.
  /// </summary>
  private static bool TryWriteSingleCopyUnpackInfo(
      List<byte> header,
      int unpackSize,
      uint unpackCrc)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(0x00);

    if (!TryWriteUInt64(header, 1))
      return false;

    // Copy coder: Method ID = 00, id size = 1, без properties.
    header.Add(0x01);
    header.Add(0x00);

    header.Add(SevenZipNid.CodersUnpackSize);

    if (!TryWriteUInt64(header, (ulong)unpackSize))
      return false;

    header.Add(SevenZipNid.Crc);
    WriteSingleDefinedStreamCrcDigest(header, unpackCrc);

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет CRC digest для одного stream-а с allAreDefined.
  /// </summary>
  private static void WriteSingleDefinedStreamCrcDigest(
      List<byte> header,
      uint crc)
  {
    header.Add(0x01);
    WriteUInt32LittleEndian(header, crc);
  }

  /// <summary>
  /// Строит next header для архива с одним пустым файлом.
  /// </summary>
  private static bool TryBuildSingleEmptyFileNextHeader(
      string fileName,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(128) { SevenZipNid.Header };

    if (!TryWriteSingleEmptyFileFilesInfo(header, fileName))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для одного пустого файла.
  /// </summary>
  private static bool TryWriteSingleEmptyFileFilesInfo(
      List<byte> header,
      string fileName)
  {
    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(SevenZipNid.EmptyStream);

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(0x80);

    header.Add(SevenZipNid.EmptyFile);

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(0x80);

    if (!TryWriteSingleFileNameProperty(header, fileName))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Строит 7z-архив с пустыми файлами и пустыми директориями.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildEmptyEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> files,
      out byte[] archive)
  {
    archive = [];

    if (!TryBuildEmptyEntriesNextHeader(files, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithNextHeader(nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит next header для архива с пустыми файлами и пустыми директориями.
  /// </summary>
  private static bool TryBuildEmptyEntriesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> files,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(128)
    {
        SevenZipNid.Header,
    };

    if (!TryWriteEmptyEntriesFilesInfo(header, files))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для пустых файлов и пустых директорий.
  /// </summary>
  private static bool TryWriteEmptyEntriesFilesInfo(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> files)
  {
    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, (ulong)files.Count))
      return false;

    header.Add(SevenZipNid.EmptyStream);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(files.Count)))
      return false;

    WriteAllTrueBitVector(header, files.Count);

    header.Add(SevenZipNid.EmptyFile);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(files.Count)))
      return false;

    WriteEmptyFileBitVector(header, files);

    if (!TryWriteFileNamesProperty(header, files))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет свойство имён для набора элементов архива.
  /// </summary>
  private static bool TryWriteFileNamesProperty(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> files)
  {
    header.Add(SevenZipNid.Name);

    List<byte[]> encodedNames = new(files.Count);
    int nameBytesLength = 0;

    for (int i = 0; i < files.Count; i++)
    {
      byte[] nameBytes = Encoding.Unicode.GetBytes(files[i].Name + "\0");

      encodedNames.Add(nameBytes);
      nameBytesLength += nameBytes.Length;
    }

    if (!TryWriteUInt64(header, (ulong)(1 + nameBytesLength)))
      return false;

    header.Add(0x00);

    for (int i = 0; i < encodedNames.Count; i++)
      header.AddRange(encodedNames[i]);

    return true;
  }

  /// <summary>
  /// Проверяет входные элементы writer-а.
  /// </summary>
  private static bool TryValidateWriterEntries(IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (entry is null || entry.Content is null)
        return false;

      if (!IsSupportedEntryName(entry.Name))
        return false;

      if (!names.Add(entry.Name))
        return false;

      if (entry.IsDirectory && entry.Content.Length != 0)
        return false;
    }

    return true;
  }

  /// <summary>
  /// Проверяет, что все элементы не содержат файловых данных.
  /// </summary>
  private static bool AllEntriesHaveNoContent(
      IReadOnlyList<SevenZipArchiveWriterEntry> files)
  {
    for (int i = 0; i < files.Count; i++)
      if (files[i].Content.Length != 0)
        return false;

    return true;
  }

  /// <summary>
  /// Пишет bit-vector EmptyFile для empty stream элементов.
  /// true означает пустой файл, false означает директорию.
  /// </summary>
  private static void WriteEmptyFileBitVector(
      List<byte> destination,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries) => WriteBitVector(destination, entries.Count, index => !entries[index].IsDirectory);

  /// <summary>
  /// Возвращает размер bit-vector в байтах.
  /// </summary>
  private static int GetBitVectorByteCount(int bitCount) => (bitCount + 7) / 8;

  /// <summary>
  /// Пишет bit-vector в 7z-порядке битов.
  /// </summary>
  private static void WriteBitVector(
      List<byte> destination,
      int bitCount,
      Func<int, bool> isBitSet)
  {
    int byteCount = GetBitVectorByteCount(bitCount);

    for (int byteIndex = 0; byteIndex < byteCount; byteIndex++)
    {
      byte value = 0;

      for (int bitIndex = 0; bitIndex < 8; bitIndex++)
      {
        int itemIndex = byteIndex * 8 + bitIndex;

        if (itemIndex >= bitCount)
          break;

        if (isBitSet(itemIndex))
          value |= (byte)(0x80 >> bitIndex);
      }

      destination.Add(value);
    }
  }

  /// <summary>
  /// Пишет bit-vector, в котором все элементы установлены в true.
  /// </summary>
  private static void WriteAllTrueBitVector(
      List<byte> destination,
      int bitCount) => WriteBitVector(destination, bitCount, _ => true);

  /// <summary>
  /// Пишет FilesInfo для одного непустого файла Copy.
  /// </summary>
  private static bool TryWriteSingleFileCopyFilesInfo(
      List<byte> header,
      string fileName,
      uint contentCrc)
  {
    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, 1))
      return false;

    if (!TryWriteSingleFileNameProperty(header, fileName))
      return false;

    if (!TryWriteSingleDefinedCrcProperty(header, contentCrc))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет свойство имени для одного файла.
  /// </summary>
  private static bool TryWriteSingleFileNameProperty(
      List<byte> header,
      string fileName)
  {
    header.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");

    if (!TryWriteUInt64(header, (ulong)(1 + nameBytes.Length)))
      return false;

    header.Add(0x00);
    header.AddRange(nameBytes);

    return true;
  }

  /// <summary>
  /// Пишет CRC-свойство для одного файла с allAreDefined.
  /// </summary>
  private static bool TryWriteSingleDefinedCrcProperty(
      List<byte> header,
      uint crc)
  {
    header.Add(SevenZipNid.Crc);

    if (!TryWriteUInt64(header, 5))
      return false;

    header.Add(0x01);
    WriteUInt32LittleEndian(header, crc);

    return true;
  }

  /// <summary>
  /// Строит каркас 7z-архива из уже подготовленного next header.
  /// </summary>
  private static byte[] BuildArchiveWithNextHeader(byte[] nextHeaderBytes) => BuildArchiveWithPackedData([], nextHeaderBytes);

  /// <summary>
  /// Строит 7z-архив из packed data и уже подготовленного next header.
  /// </summary>
  private static byte[] BuildArchiveWithPackedData(
      byte[] packedData,
      byte[] nextHeaderBytes)
  {
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedData.Length,
        NextHeaderSize: (ulong)nextHeaderBytes.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[
        SevenZipSignatureHeader.Size
        + packedData.Length
        + nextHeaderBytes.Length];

    signatureHeader.Write(archive);

    packedData.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));

    nextHeaderBytes.CopyTo(
        archive.AsSpan(SevenZipSignatureHeader.Size + packedData.Length));

    return archive;
  }

  /// <summary>
  /// Проверяет имя элемента для текущих минимальных writer-сценариев.
  /// </summary>
  private static bool IsSupportedEntryName(string entryName)
  {
    return !string.IsNullOrWhiteSpace(entryName)
        && entryName.IndexOf('\0') < 0
        && entryName.IndexOf('/') < 0
        && entryName.IndexOf('\\') < 0
        && !ContainsUnsupportedWindowsEntryCharacter(entryName)
        && !HasUnsupportedTrailingEntryCharacter(entryName)
        && !IsWindowsReservedEntryName(entryName);
  }

  /// <summary>
  /// Проверяет завершающий символ имени, который опасен для Windows-путей.
  /// </summary>
  private static bool HasUnsupportedTrailingEntryCharacter(string entryName)
  {
    char lastCharacter = entryName[^1];

    return lastCharacter == '.'
        || char.IsWhiteSpace(lastCharacter);
  }

  /// <summary>
  /// Проверяет наличие символов, недопустимых для имени Windows-файла.
  /// </summary>
  private static bool ContainsUnsupportedWindowsEntryCharacter(string entryName)
  {
    for (int i = 0; i < entryName.Length; i++)
    {
      char character = entryName[i];

      if (character < ' ')
        return true;

      if (character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
        return true;
    }

    return false;
  }

  /// <summary>
  /// Проверяет, является ли имя зарезервированным Windows-именем.
  /// </summary>
  private static bool IsWindowsReservedEntryName(string entryName)
  {
    int dotIndex = entryName.IndexOf('.');
    string baseName = dotIndex >= 0
        ? entryName[..dotIndex]
        : entryName;

    return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
        || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
        || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
        || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
        || IsNumberedWindowsReservedEntryName(baseName, "COM")
        || IsNumberedWindowsReservedEntryName(baseName, "LPT");
  }

  /// <summary>
  /// Проверяет имена вида COM1..COM9 и LPT1..LPT9.
  /// </summary>
  private static bool IsNumberedWindowsReservedEntryName(
      string baseName,
      string prefix)
  {
    return baseName.Length == 4
        && baseName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && baseName[3] >= '1'
        && baseName[3] <= '9';
  }

  /// <summary>
  /// Пишет UInt64 в 7z-представлении в наращиваемый буфер.
  /// </summary>
  private static bool TryWriteUInt64(List<byte> destination, ulong value)
  {
    SevenZipEncodedUInt64.WriteResult result = SevenZipEncodedUInt64.TryWrite(
        destination,
        value,
        out _);

    return result == SevenZipEncodedUInt64.WriteResult.Ok;
  }

  /// <summary>
  /// Пишет UInt32 в little-endian представлении.
  /// </summary>
  private static void WriteUInt32LittleEndian(List<byte> destination, uint value)
  {
    destination.Add((byte)value);
    destination.Add((byte)(value >> 8));
    destination.Add((byte)(value >> 16));
    destination.Add((byte)(value >> 24));
  }
}
