using System.Text;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Строит 7z-архивы для поддерживаемых writer-сценариев.
/// </summary>
public static class SevenZipArchiveWriter
{
  private const uint WindowsFileAttributeDirectory = 0x00000010;
  private const uint WindowsFileAttributeArchive = 0x00000020;

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

    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    return BuildCopyEntriesArchive(entries, out archive);
  }

  /// <summary>
  /// Строит 7z-архив с непустыми Copy-файлами.
  /// Поддерживает как набор только из непустых файлов, так и mixed-набор с empty entries.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildCopyEntriesArchive(IReadOnlyList<SevenZipArchiveWriterEntry> entries, out byte[] archive)
  {
    archive = [];

    if (!TryBuildCopyPackedData(
            entries,
            out byte[] packedData,
            out int[] sizes,
            out uint[] crcs))
      return SevenZipArchiveWriteResult.NotSupported;

    if (!TryBuildCopyEntriesNextHeader(
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
  /// Строит next header для Copy-сценария.
  /// </summary>
  private static bool TryBuildCopyEntriesNextHeader(
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

    if (!TryWriteCopyStreamsPackInfo(header, sizes, crcs))
      return false;

    if (!TryWriteCopyFoldersUnpackInfo(header, sizes, crcs))
      return false;

    header.Add(SevenZipNid.End);

    if (AllEntriesAreNonEmptyFiles(entries))
    {
      if (!TryWriteAllNonEmptyCopyEntriesFilesInfo(header, entries, crcs))
        return false;
    }
    else if (!TryWriteMixedCopyEntriesFilesInfo(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для смешанного набора empty entries и непустых Copy-файлов.
  /// </summary>
  private static bool TryWriteMixedCopyEntriesFilesInfo(List<byte> header, IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    if (!TryWriteFilesInfoStart(header, entries.Count))
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

    if (!TryWriteMTimeProperty(header, entries))
      return false;

    if (!TryWriteWinAttributesProperty(header, entries))
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
  private static bool TryWriteMixedCopyEntriesCrcProperty(List<byte> header, IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    bool[] defined = new bool[entries.Count];
    uint[] crcs = new uint[entries.Count];

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      defined[i] = true;
      crcs[i] = Crc32.Compute(entry.Content);
    }

    return TryWriteFilesInfoCrcProperty(header, defined, crcs);
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
  /// Пишет PackInfo для packed stream-ов Copy-сценария.
  /// </summary>
  private static bool TryWriteCopyStreamsPackInfo(
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
  /// Пишет UnpackInfo для folder-ов Copy-сценария.
  /// </summary>
  private static bool TryWriteCopyFoldersUnpackInfo(
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
  /// Пишет начало FilesInfo и количество entry.
  /// </summary>
  private static bool TryWriteFilesInfoStart(
      List<byte> header,
      int entryCount)
  {
    header.Add(SevenZipNid.FilesInfo);

    return TryWriteUInt64(header, (ulong)entryCount);
  }

  /// <summary>
  /// Пишет FilesInfo для сценария, где все entry являются непустыми Copy-файлами.
  /// </summary>
  private static bool TryWriteAllNonEmptyCopyEntriesFilesInfo(List<byte> header, IReadOnlyList<SevenZipArchiveWriterEntry> entries, uint[] crcs)
  {
    if (!TryWriteFilesInfoStart(header, entries.Count))
      return false;

    if (!TryWriteFileNamesProperty(header, entries))
      return false;

    if (!TryWriteMTimeProperty(header, entries))
      return false;

    if (!TryWriteWinAttributesProperty(header, entries))
      return false;

    if (!TryWriteDefinedCrcProperty(header, crcs))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет CRC-свойство FilesInfo с defined bit-vector при необходимости.
  /// </summary>
  private static bool TryWriteFilesInfoCrcProperty(
      List<byte> header,
      bool[] defined,
      uint[] crcs)
  {
    if (defined.Length != crcs.Length)
      return false;

    bool allAreDefined = true;
    int definedCount = 0;

    for (int i = 0; i < defined.Length; i++)
      if (defined[i])
        definedCount++;
      else
        allAreDefined = false;

    header.Add(SevenZipNid.Crc);

    ulong propertySize =
        1UL
        + (allAreDefined ? 0UL : (ulong)GetBitVectorByteCount(defined.Length))
        + ((ulong)definedCount * 4UL);

    if (!TryWriteUInt64(header, propertySize))
      return false;

    header.Add(allAreDefined ? (byte)0x01 : (byte)0x00);

    if (!allAreDefined)
      WriteDefinedBitVector(header, defined);

    for (int i = 0; i < crcs.Length; i++)
    {
      if (!defined[i])
        continue;

      WriteUInt32LittleEndian(header, crcs[i]);
    }

    return true;
  }

  /// <summary>
  /// Пишет CRC-свойство FilesInfo для набора entry с allAreDefined.
  /// </summary>
  private static bool TryWriteDefinedCrcProperty(
    List<byte> header,
    uint[] crcs)
  {
    bool[] defined = new bool[crcs.Length];

    for (int i = 0; i < defined.Length; i++)
      defined[i] = true;

    return TryWriteFilesInfoCrcProperty(header, defined, crcs);
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
  /// Строит 7z-архив с пустыми файлами и пустыми директориями.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildEmptyEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (!TryBuildEmptyEntriesNextHeader(entries, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithNextHeader(nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит next header для архива с пустыми файлами и пустыми директориями.
  /// </summary>
  private static bool TryBuildEmptyEntriesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(128)
    {
        SevenZipNid.Header,
    };

    if (!TryWriteEmptyEntriesFilesInfo(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет FilesInfo для пустых файлов и пустых директорий.
  /// </summary>
  private static bool TryWriteEmptyEntriesFilesInfo(List<byte> header, IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    if (!TryWriteFilesInfoStart(header, entries.Count))
      return false;

    header.Add(SevenZipNid.EmptyStream);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(entries.Count)))
      return false;

    WriteAllTrueBitVector(header, entries.Count);

    header.Add(SevenZipNid.EmptyFile);

    if (!TryWriteUInt64(header, (ulong)GetBitVectorByteCount(entries.Count)))
      return false;

    WriteEmptyFileBitVector(header, entries);

    if (!TryWriteFileNamesProperty(header, entries))
      return false;

    if (!TryWriteMTimeProperty(header, entries))
      return false;

    if (!TryWriteWinAttributesProperty(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет свойство имён для набора элементов архива.
  /// </summary>
  private static bool TryWriteFileNamesProperty(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    header.Add(SevenZipNid.Name);

    List<byte[]> encodedNames = new(entries.Count);
    int nameBytesLength = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      byte[] nameBytes = Encoding.Unicode.GetBytes(entries[i].Name + "\0");

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
  private static bool TryValidateWriterEntries(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (entry is null || entry.Content is null)
        return false;

      if (!IsSupportedEntryPath(entry.Name))
        return false;

      if (!names.Add(entry.Name))
        return false;

      if (entry.IsDirectory && entry.Content.Length != 0)
        return false;

      if (!IsSupportedWindowsAttributes(entry))
        return false;

      if (!IsSupportedLastWriteTimeUtc(entry.LastWriteTimeUtc))
        return false;
    }

    return !HasNonDirectoryParentConflict(entries);
  }

  /// <summary>
  /// Проверяет согласованность Windows attributes с типом entry.
  /// </summary>
  private static bool IsSupportedWindowsAttributes(SevenZipArchiveWriterEntry entry)
  {
    if (!entry.WindowsAttributes.HasValue)
      return true;

    bool hasDirectoryAttribute =
        (entry.WindowsAttributes.Value & WindowsFileAttributeDirectory) != 0;

    return entry.IsDirectory
        ? hasDirectoryAttribute
        : !hasDirectoryAttribute;
  }

  /// <summary>
  /// Проверяет, что все элементы не содержат файловых данных.
  /// </summary>
  private static bool AllEntriesHaveNoContent(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    for (int i = 0; i < entries.Count; i++)
      if (entries[i].Content.Length != 0)
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
  /// Проверяет путь entry для текущих writer-сценариев.
  /// </summary>
  private static bool IsSupportedEntryPath(string entryPath)
  {
    if (string.IsNullOrWhiteSpace(entryPath))
      return false;

    if (entryPath.Contains('\\'))
      return false;

    if (entryPath[0] == '/' || entryPath[^1] == '/')
      return false;

    string[] segments = entryPath.Split('/');

    for (int i = 0; i < segments.Length; i++)
      if (!IsSupportedEntryPathSegment(segments[i]))
        return false;

    return true;
  }

  /// <summary>
  /// Проверяет один сегмент пути entry.
  /// </summary>
  private static bool IsSupportedEntryPathSegment(string segment)
  {
    return !string.IsNullOrWhiteSpace(segment)
        && !IsSpecialEntryPathSegment(segment)
        && !ContainsUnsupportedWindowsEntryCharacter(segment)
        && !HasUnsupportedTrailingEntryCharacter(segment)
        && !IsWindowsReservedEntryName(segment);
  }

  /// <summary>
  /// Проверяет специальные сегменты пути.
  /// </summary>
  private static bool IsSpecialEntryPathSegment(string segment) => segment == "." || segment == "..";

  /// <summary>
  /// Проверяет конфликт, когда родительский путь существует как файл.
  /// </summary>
  private static bool HasNonDirectoryParentConflict(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    Dictionary<string, bool> directoryByPath = new(StringComparer.OrdinalIgnoreCase);

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      directoryByPath[entry.Name] = entry.IsDirectory;
    }

    for (int i = 0; i < entries.Count; i++)
    {
      string entryPath = entries[i].Name;
      int slashIndex = entryPath.IndexOf('/');

      while (slashIndex >= 0)
      {
        string parentPath = entryPath[..slashIndex];

        if (directoryByPath.TryGetValue(parentPath, out bool isDirectory) && !isDirectory)
          return true;

        slashIndex = entryPath.IndexOf('/', slashIndex + 1);
      }
    }

    return false;
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
  /// Пишет WinAttrib для всех entry.
  /// </summary>
  private static bool TryWriteWinAttributesProperty(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    header.Add(SevenZipNid.WinAttrib);

    ulong propertySize = 2UL + ((ulong)entries.Count * 4UL);

    if (!TryWriteUInt64(header, propertySize))
      return false;

    // AllAreDefined = true.
    header.Add(0x01);

    // External = false.
    header.Add(0x00);

    for (int i = 0; i < entries.Count; i++)
      WriteUInt32LittleEndian(header, GetWindowsAttributes(entries[i]));

    return true;
  }

  /// <summary>
  /// Проверяет, что время последней записи задано как UTC и может быть представлено в FILETIME.
  /// </summary>
  private static bool IsSupportedLastWriteTimeUtc(DateTime? lastWriteTimeUtc)
  {
    if (!lastWriteTimeUtc.HasValue)
      return true;

    DateTime value = lastWriteTimeUtc.Value;

    if (value.Kind != DateTimeKind.Utc)
      return false;

    try
    {
      _ = value.ToFileTimeUtc();
      return true;
    }
    catch (ArgumentOutOfRangeException)
    {
      return false;
    }
  }

  /// <summary>
  /// Пишет MTime для entry, у которых задан LastWriteTimeUtc.
  /// </summary>
  private static bool TryWriteMTimeProperty(
      List<byte> header,
      IReadOnlyList<SevenZipArchiveWriterEntry> entries)
  {
    bool[] defined = new bool[entries.Count];
    ulong[] times = new ulong[entries.Count];

    int definedCount = 0;
    bool allAreDefined = true;

    for (int i = 0; i < entries.Count; i++)
    {
      DateTime? lastWriteTimeUtc = entries[i].LastWriteTimeUtc;

      if (!lastWriteTimeUtc.HasValue)
      {
        allAreDefined = false;
        continue;
      }

      defined[i] = true;
      times[i] = (ulong)lastWriteTimeUtc.Value.ToFileTimeUtc();
      definedCount++;
    }

    if (definedCount == 0)
      return true;

    header.Add(SevenZipNid.MTime);

    ulong propertySize =
        1UL
        + (allAreDefined ? 0UL : (ulong)GetBitVectorByteCount(entries.Count))
        + 1UL
        + ((ulong)definedCount * 8UL);

    if (!TryWriteUInt64(header, propertySize))
      return false;

    header.Add(allAreDefined ? (byte)0x01 : (byte)0x00);

    if (!allAreDefined)
      WriteDefinedBitVector(header, defined);

    // External = false.
    header.Add(0x00);

    for (int i = 0; i < entries.Count; i++)
    {
      if (!defined[i])
        continue;

      WriteUInt64LittleEndian(header, times[i]);
    }

    return true;
  }

  /// <summary>
  /// Пишет UInt64 в little-endian представлении.
  /// </summary>
  private static void WriteUInt64LittleEndian(List<byte> destination, ulong value)
  {
    destination.Add((byte)value);
    destination.Add((byte)(value >> 8));
    destination.Add((byte)(value >> 16));
    destination.Add((byte)(value >> 24));
    destination.Add((byte)(value >> 32));
    destination.Add((byte)(value >> 40));
    destination.Add((byte)(value >> 48));
    destination.Add((byte)(value >> 56));
  }

  /// <summary>
  /// Возвращает Windows attributes для entry.
  /// </summary>
  private static uint GetWindowsAttributes(SevenZipArchiveWriterEntry entry)
  {
    return entry.WindowsAttributes
        ?? GetDefaultWindowsAttributes(entry);
  }

  /// <summary>
  /// Возвращает базовые Windows attributes для entry.
  /// </summary>
  private static uint GetDefaultWindowsAttributes(SevenZipArchiveWriterEntry entry)
  {
    return entry.IsDirectory
        ? WindowsFileAttributeDirectory
        : WindowsFileAttributeArchive;
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
