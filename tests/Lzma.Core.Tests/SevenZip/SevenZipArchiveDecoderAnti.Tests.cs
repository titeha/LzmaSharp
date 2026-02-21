using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderAntiTests
{
  [Fact]
  public void DecodeAllFilesToArray_ЕслиЕстьAntiItem_ВозвращаетNotSupported()
  {
    byte[] archive = Build7z_OneEmptyAntiFile_WithName("anti");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Empty(files);
  }

  private static byte[] Build7z_OneEmptyAntiFile_WithName(string name)
  {
    List<byte> h = new(128)
    {
      SevenZipNid.Header,
      SevenZipNid.FilesInfo
    };
    WriteU64(h, 1); // NumFiles = 1

    // kEmptyStream: [true] => 0x80
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x80);

    // kAnti: для empty-stream элементов. [true] => 0x80
    h.Add(SevenZipNid.Anti);
    WriteU64(h, 1);
    h.Add(0x80);

    // kName
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
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
