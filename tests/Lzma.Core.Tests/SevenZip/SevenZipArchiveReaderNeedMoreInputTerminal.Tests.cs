using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderNeedMoreInputTerminalTests
{
  [Fact]
  public void Read_ПустойNextHeader_ЭтоInvalidData_ИТерминальноеСостояние()
  {
    byte[] file = BuildArchive(nextHeaderOffset: 0, nextHeaderBytes: []);

    var reader = new SevenZipArchiveReader();
    var res = reader.Read(file, out int consumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res);
    Assert.Equal(file.Length, consumed);

    Assert.NotNull(reader.SignatureHeader);
    Assert.Null(reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.Empty(reader.NextHeaderBytes.ToArray());

    var res2 = reader.Read([], out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_HeaderБезEnd_ЭтоInvalidData_ИТерминальноеСостояние()
  {
    byte[] file = BuildArchive(
      nextHeaderOffset: 0,
      nextHeaderBytes:
      [
        SevenZipNid.Header,
      ]);

    var reader = new SevenZipArchiveReader();
    var res = reader.Read(file, out int consumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res);
    Assert.Equal(file.Length, consumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.Equal(new byte[] { SevenZipNid.Header }, reader.NextHeaderBytes.ToArray());

    var res2 = reader.Read([], out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void DecodeToArray_ПоврежденныйNextHeaderНеДолженВозвращатьNeedMoreData()
  {
    byte[] file = BuildArchive(
      nextHeaderOffset: 0,
      nextHeaderBytes:
      [
        SevenZipNid.Header,
      ]);

    var res = SevenZipArchiveDecoder.DecodeToArray(file, out SevenZipDecodedFile[] files, out int consumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, res);
    Assert.Equal(file.Length, consumed);
    Assert.Empty(files);
  }

  private static byte[] BuildArchive(ulong nextHeaderOffset, byte[] nextHeaderBytes)
  {
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);
    var signatureHeader = new SevenZipSignatureHeader(nextHeaderOffset, (ulong)nextHeaderBytes.Length, nextHeaderCrc);

    byte[] file = new byte[SevenZipSignatureHeader.Size + (int)nextHeaderOffset + nextHeaderBytes.Length];
    signatureHeader.Write(file.AsSpan(0, SevenZipSignatureHeader.Size));

    if (nextHeaderBytes.Length > 0)
      nextHeaderBytes.CopyTo(file, SevenZipSignatureHeader.Size + (int)nextHeaderOffset);

    return file;
  }
}
