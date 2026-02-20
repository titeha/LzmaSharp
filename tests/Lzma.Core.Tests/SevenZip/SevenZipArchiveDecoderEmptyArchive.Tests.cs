using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderEmptyArchiveTests
{
  [Fact]
  public void DecodeAllFilesToArray_ПустойАрхив_ВозвращаетOk_ИПустойМассив()
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

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Empty(files);
  }
}
