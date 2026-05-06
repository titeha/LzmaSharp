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

    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: 0,
        NextHeaderSize: (ulong)nextHeaderBytes.Length,
        NextHeaderCrc: nextHeaderCrc);

    archive = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    signatureHeader.Write(archive);
    nextHeaderBytes.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));

    return SevenZipArchiveWriteResult.Ok;
  }
}
