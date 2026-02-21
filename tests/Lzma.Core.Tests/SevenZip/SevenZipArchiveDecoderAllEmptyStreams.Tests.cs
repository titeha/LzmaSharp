using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderAllEmptyStreamsTests
{
  [Fact]
  public void DecodeAllFilesToArray_ТолькоПустыеФайлы_БезStreamsInfo_ВозвращаетOk()
  {
    byte[] archive = Build7z_OnlyFilesInfo_AllEmptyStreams("a", "b");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);

    Assert.Equal("a", files[0].Name);
    Assert.Empty(files[0].Bytes);

    Assert.Equal("b", files[1].Name);
    Assert.Empty(files[1].Bytes);
  }

  private static byte[] Build7z_OnlyFilesInfo_AllEmptyStreams(string name1, string name2)
  {
    List<byte> h =
    [
      SevenZipNid.Header,
      // FilesInfo
      SevenZipNid.FilesInfo,
    ];
    WriteU64(h, 2); // NumFiles = 2

    // kEmptyStream: оба файла пустые => [true, true] => 0xC0 (0x80 | 0x40)
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0xC0);

    // kName
    h.Add(SevenZipNid.Name);
    byte[] namesBytes = Encoding.Unicode.GetBytes(name1 + "\0" + name2 + "\0");
    WriteU64(h, (ulong)(1 + namesBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(namesBytes);

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
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
