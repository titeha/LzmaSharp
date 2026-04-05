using System.Buffers.Binary;
using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSignatureHeaderTests
{
  [Fact]
  public void TryRead_EmptyBuffer_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    var res = SevenZipSignatureHeader.TryRead(
      [],
      out SevenZipSignatureHeader header,
      out int consumed);

    Assert.Equal(SevenZipSignatureHeader.ReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Equal(default(SevenZipSignatureHeader), header);
  }

  [Fact]
  public void TryRead_TruncatedBuffer_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    byte[] data = BuildValidHeaderBytes(
      nextHeaderOffset: 1,
      nextHeaderSize: 2,
      nextHeaderCrc: 0x11223344u);

    Array.Resize(ref data, SevenZipSignatureHeader.Size - 1);

    var res = SevenZipSignatureHeader.TryRead(
      data,
      out SevenZipSignatureHeader header,
      out int consumed);

    Assert.Equal(SevenZipSignatureHeader.ReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Equal(default(SevenZipSignatureHeader), header);
  }

  [Fact]
  public void TryRead_InvalidSignature_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data = BuildValidHeaderBytes(
      nextHeaderOffset: 3,
      nextHeaderSize: 4,
      nextHeaderCrc: 0xAABBCCDDu);

    data[0] ^= 0xFF;

    var res = SevenZipSignatureHeader.TryRead(
      data,
      out SevenZipSignatureHeader header,
      out int consumed);

    Assert.Equal(SevenZipSignatureHeader.ReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Equal(default(SevenZipSignatureHeader), header);
  }

  [Fact]
  public void TryRead_InvalidStartHeaderCrc_ReturnsInvalidData_AndConsumesNothing()
  {
    byte[] data = BuildValidHeaderBytes(
      nextHeaderOffset: 5,
      nextHeaderSize: 6,
      nextHeaderCrc: 0x55667788u);

    data[8] ^= 0xFF;

    var res = SevenZipSignatureHeader.TryRead(
      data,
      out SevenZipSignatureHeader header,
      out int consumed);

    Assert.Equal(SevenZipSignatureHeader.ReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Equal(default(SevenZipSignatureHeader), header);
  }

  [Fact]
  public void Write_WhenStartHeaderCrcIsZero_ComputesCrc_AndRoundTrips()
  {
    var header = new SevenZipSignatureHeader(
      SevenZipSignatureHeader.MajorVersion,
      SevenZipSignatureHeader.MinorVersion,
      0u,
      0x0102030405060708UL,
      0x1112131415161718UL,
      0x99AABBCCu);

    byte[] data = new byte[SevenZipSignatureHeader.Size];
    header.Write(data);

    uint writtenStartHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
    Assert.NotEqual(0u, writtenStartHeaderCrc);
    Assert.Equal(Crc32.Compute(header.GetStartHeaderBytes()), writtenStartHeaderCrc);

    var res = SevenZipSignatureHeader.TryRead(
      data,
      out SevenZipSignatureHeader parsed,
      out int consumed);

    Assert.Equal(SevenZipSignatureHeader.ReadResult.Ok, res);
    Assert.Equal(SevenZipSignatureHeader.Size, consumed);

    Assert.Equal(header.VersionMajor, parsed.VersionMajor);
    Assert.Equal(header.VersionMinor, parsed.VersionMinor);
    Assert.Equal(writtenStartHeaderCrc, parsed.StartHeaderCrc);
    Assert.Equal(header.NextHeaderOffset, parsed.NextHeaderOffset);
    Assert.Equal(header.NextHeaderSize, parsed.NextHeaderSize);
    Assert.Equal(header.NextHeaderCrc, parsed.NextHeaderCrc);
  }

  [Fact]
  public void Write_IfBufferTooSmall_ThrowsArgumentOutOfRangeException()
  {
    var header = new SevenZipSignatureHeader(
      1,
      2,
      3);

    Assert.Throws<ArgumentOutOfRangeException>(() => header.Write(new byte[SevenZipSignatureHeader.Size - 1]));
  }

  [Fact]
  public void GetStartHeaderBytes_Returns20BytesInLittleEndianOrder()
  {
    var header = new SevenZipSignatureHeader(
      7,
      9,
      0x01020304u,
      0x1122334455667788UL,
      0x8877665544332211UL,
      0xA1B2C3D4u);

    byte[] startHeaderBytes = header.GetStartHeaderBytes();

    Assert.Equal(20, startHeaderBytes.Length);
    Assert.Equal(0x1122334455667788UL, BinaryPrimitives.ReadUInt64LittleEndian(startHeaderBytes.AsSpan(0, 8)));
    Assert.Equal(0x8877665544332211UL, BinaryPrimitives.ReadUInt64LittleEndian(startHeaderBytes.AsSpan(8, 8)));
    Assert.Equal(0xA1B2C3D4u, BinaryPrimitives.ReadUInt32LittleEndian(startHeaderBytes.AsSpan(16, 4)));
  }

  private static byte[] BuildValidHeaderBytes(
    ulong nextHeaderOffset,
    ulong nextHeaderSize,
    uint nextHeaderCrc)
  {
    var header = new SevenZipSignatureHeader(
      nextHeaderOffset,
      nextHeaderSize,
      nextHeaderCrc);

    byte[] data = new byte[SevenZipSignatureHeader.Size];
    header.Write(data);
    return data;
  }
}
