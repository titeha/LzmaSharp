using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSingleEmptyDirectoryTests
{
  [Fact]
  public void DecodeSingleFileToArray_ОдинПустойКаталог_ReturnsNotSupported()
  {
    byte[] archive = Build7z_OneEmptyDirectory_WithName("dir");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] fileBytes,
      out string fileName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeToEntries_ОдинПустойКаталог_ПомечаетсяКакDirectory()
  {
    byte[] archive = Build7z_OneEmptyDirectory_WithName("dir");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(
      archive,
      out SevenZipDecodedEntry[] entries,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(entries);
    Assert.Equal("dir", entries[0].Name);
    Assert.True(entries[0].IsDirectory);
    Assert.Empty(entries[0].Bytes);
  }

  private static byte[] Build7z_OneEmptyDirectory_WithName(string name)
  {
    List<byte> h = new(128)
    {
      SevenZipNid.Header,
      SevenZipNid.FilesInfo
    };

    WriteU64(h, 1); // NumFiles

    // kEmptyStream: [true] => 0x80
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x80);

    // ВАЖНО: kEmptyFile здесь специально НЕ пишем.
    // Отсутствие kEmptyFile означает, что EmptyStream трактуется как каталог.

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(name + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    byte[] nextHeader = [.. h];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: 0,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeader.Length];
    sig.Write(archive);
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    return archive;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
