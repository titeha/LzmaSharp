using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderNextHeaderReaderNotSupportedTests
{
  [Fact]
  public void Read_NextHeaderOffsetExceedsInternalLimit_ReturnsNotSupported_AndBecomesTerminal()
  {
    byte[] archive = BuildArchiveWithSignatureHeader(
      nextHeaderOffset: 64UL * 1024 * 1024 + 1,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(SevenZipSignatureHeader.Size, bytesConsumed);
    Assert.True(reader.SignatureHeader.HasValue);
    Assert.Null(reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.PackedStreams.IsEmpty);
    Assert.True(reader.NextHeaderBytes.IsEmpty);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    result = reader.Read([], out bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void Read_NextHeaderSizeTooLarge_ReturnsNotSupported_AndBecomesTerminal()
  {
    byte[] archive = BuildArchiveWithSignatureHeader(
      nextHeaderOffset: 0,
      nextHeaderSize: (ulong)int.MaxValue + 1,
      nextHeaderCrc: 0);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(SevenZipSignatureHeader.Size, bytesConsumed);
    Assert.True(reader.SignatureHeader.HasValue);
    Assert.Null(reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.PackedStreams.IsEmpty);
    Assert.True(reader.NextHeaderBytes.IsEmpty);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    result = reader.Read([], out bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(0, bytesConsumed);
  }

  private static byte[] BuildArchiveWithSignatureHeader(
    ulong nextHeaderOffset,
    ulong nextHeaderSize,
    uint nextHeaderCrc)
  {
    byte[] archive = new byte[SevenZipSignatureHeader.Size];
    new SevenZipSignatureHeader(nextHeaderOffset, nextHeaderSize, nextHeaderCrc).Write(archive);
    return archive;
  }
}
