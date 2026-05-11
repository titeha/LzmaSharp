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
      IReadOnlyList<SevenZipArchiveWriterEntry> files,
      out byte[] archive)
  {
    archive = [];

    if (files is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (files.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(files))
      return SevenZipArchiveWriteResult.InvalidData;

    if (files.Count == 1)
    {
      SevenZipArchiveWriterEntry file = files[0];

      if (file.IsDirectory)
        return BuildEmptyEntriesArchive(files, out archive);

      if (file.Content.Length == 0)
        return BuildSingleEmptyFileArchive(file.Name, out archive);

      return BuildSingleFileCopyArchive(file.Name, file.Content, out archive);
    }

    if (AllEntriesHaveNoContent(files))
      return BuildEmptyEntriesArchive(files, out archive);

    return SevenZipArchiveWriteResult.NotSupported;
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
      IReadOnlyList<SevenZipArchiveWriterEntry> files)
  {
    int byteCount = GetBitVectorByteCount(files.Count);

    for (int byteIndex = 0; byteIndex < byteCount; byteIndex++)
    {
      byte value = 0;

      for (int bitIndex = 0; bitIndex < 8; bitIndex++)
      {
        int itemIndex = byteIndex * 8 + bitIndex;

        if (itemIndex >= files.Count)
          break;

        if (!files[itemIndex].IsDirectory)
          value |= (byte)(0x80 >> bitIndex);
      }

      destination.Add(value);
    }
  }

  /// <summary>
  /// Возвращает размер bit-vector в байтах.
  /// </summary>
  private static int GetBitVectorByteCount(int bitCount)
  {
    return (bitCount + 7) / 8;
  }

  /// <summary>
  /// Пишет bit-vector, в котором все элементы установлены в true.
  /// </summary>
  private static void WriteAllTrueBitVector(
      List<byte> destination,
      int bitCount)
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

        value |= (byte)(0x80 >> bitIndex);
      }

      destination.Add(value);
    }
  }

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
        && entryName.IndexOf('\\') < 0;
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
