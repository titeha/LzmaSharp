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
  public static SevenZipArchiveWriteResult BuildEmptyArchive(out byte[] archive)
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
  public static SevenZipArchiveWriteResult BuildSingleEmptyFileArchive(
      string fileName,
      out byte[] archive)
  {
    archive = [];

    if (!IsSupportedSingleFileName(fileName))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!TryBuildSingleEmptyFileNextHeader(fileName, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithNextHeader(nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Строит 7z-архив для поддерживаемого набора файлов.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildArchive(
      IReadOnlyList<SevenZipArchiveWriterFile> files,
      out byte[] archive)
  {
    archive = [];

    if (files is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (files.Count == 0)
      return BuildEmptyArchive(out archive);

    if (files.Count != 1)
      return SevenZipArchiveWriteResult.NotSupported;

    SevenZipArchiveWriterFile file = files[0];

    if (file is null || file.Content is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (file.Content.Length == 0)
      return BuildSingleEmptyFileArchive(file.Name, out archive);

    return BuildSingleFileCopyArchive(file.Name, file.Content, out archive);
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

    if (!IsSupportedSingleFileName(fileName))
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

        SevenZipNid.PackInfo,
    };

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(SevenZipNid.Size);

    if (!TryWriteUInt64(header, (ulong)contentLength))
      return false;

    header.Add(SevenZipNid.Crc);
    header.Add(0x01);
    WriteUInt32LittleEndian(header, contentCrc);

    header.Add(SevenZipNid.End);

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

    if (!TryWriteUInt64(header, (ulong)contentLength))
      return false;

    header.Add(SevenZipNid.Crc);
    header.Add(0x01);
    WriteUInt32LittleEndian(header, contentCrc);

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.FilesInfo);

    if (!TryWriteUInt64(header, 1))
      return false;

    header.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");

    if (!TryWriteUInt64(header, (ulong)(1 + nameBytes.Length)))
      return false;

    header.Add(0x00);
    header.AddRange(nameBytes);

    header.Add(SevenZipNid.End);

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Строит next header для архива с одним пустым файлом.
  /// </summary>
  private static bool TryBuildSingleEmptyFileNextHeader(
      string fileName,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(128)
    {
      SevenZipNid.Header,
      SevenZipNid.FilesInfo,
    };

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

    header.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");

    if (!TryWriteUInt64(header, (ulong)(1 + nameBytes.Length)))
      return false;

    header.Add(0x00);
    header.AddRange(nameBytes);

    header.Add(SevenZipNid.End);
    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

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
  /// Проверяет имя файла для первого минимального writer-сценария.
  /// </summary>
  private static bool IsSupportedSingleFileName(string fileName)
  {
    return !string.IsNullOrEmpty(fileName)
        && fileName.IndexOf('\0') < 0
        && fileName.IndexOf('/') < 0
        && fileName.IndexOf('\\') < 0;
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
