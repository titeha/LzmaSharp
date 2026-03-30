using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderEmptyArchiveTests
{
  [Fact]
  public void DecodeAllFilesToArray_ПустойАрхив_ВозвращаетOk_ИПустойМассив()
  {
    byte[] archive = CreateEmptyArchive();

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_ПустойАрхив_ВозвращаетOk_ИПустойМассив()
  {
    byte[] archive = CreateEmptyArchive();

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToEntries_ПустойАрхив_ВозвращаетOk_ИПустойМассив()
  {
    byte[] archive = CreateEmptyArchive();

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeSingleFileToArray_ПустойАрхив_ВозвращаетNotSupported()
  {
    byte[] archive = CreateEmptyArchive();

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void ExtractToDirectory_ПустойАрхив_СоздаётПапку_ИВозвращаетOk()
  {
    byte[] archive = CreateEmptyArchive();

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderEmptyArchiveTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.True(Directory.Exists(root));
      Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] CreateEmptyArchive()
  {
    // Минимальный Header: [Header, End]
    byte[] nextHeaderBytes =
    [
        SevenZipNid.Header,
            SevenZipNid.End,
        ];

    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: 0,
        NextHeaderSize: (ulong)nextHeaderBytes.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];
    sig.Write(archive);
    nextHeaderBytes.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));

    return archive;
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
  }
}
