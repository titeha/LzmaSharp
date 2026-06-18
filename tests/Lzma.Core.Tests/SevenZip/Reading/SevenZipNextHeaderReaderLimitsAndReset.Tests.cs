using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipNextHeaderReaderLimitsAndResetTests
{
  [Fact]
  public void SignatureHeader_GetterBeforeSuccessfulRead_ThrowsInvalidOperationException()
  {
    var reader = new SevenZipNextHeaderReader();

    Assert.False(reader.HasSignatureHeader);
    Assert.Throws<InvalidOperationException>(() => _ = reader.SignatureHeader);
  }

  [Fact]
  public void Read_ReturnsNotSupported_IfPackedStreamsSizeExceedsInternalLimit()
  {
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: (ulong)(64 * 1024 * 1024) + 1UL,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    var res = reader.Read(data, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal((ulong)(64 * 1024 * 1024) + 1UL, reader.SignatureHeader.NextHeaderOffset);

    var res2 = reader.Read(data, out int consumed2);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_ReturnsNotSupported_IfNextHeaderSizeExceedsIntMaxValue()
  {
    byte[] data = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0,
      nextHeaderSize: (ulong)int.MaxValue + 1UL,
      nextHeaderCrc: 0);

    var reader = new SevenZipNextHeaderReader();

    var res = reader.Read(data, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res);
    Assert.Equal(data.Length, consumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal((ulong)int.MaxValue + 1UL, reader.SignatureHeader.NextHeaderSize);

    var res2 = reader.Read(data, out int consumed2);

    Assert.Equal(SevenZipNextHeaderReadResult.NotSupported, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_AfterOk_ReturnsSameResult_AndConsumesNothing()
  {
    byte[] nextHeader = [0x01, 0x02, 0x03];
    byte[] file = Build7zFile(
      nextHeaderOffset: 0,
      nextHeader);

    var reader = new SevenZipNextHeaderReader();

    var res1 = reader.Read(file, out int consumed1);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res1);
    Assert.Equal(file.Length, consumed1);

    var res2 = reader.Read(file, out int consumed2);
    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Reset_AfterInvalidData_AllowsReadingValidFileAgain()
  {
    byte[] invalidData = BuildSignatureHeaderOnly(
      nextHeaderOffset: 0,
      nextHeaderSize: 0,
      nextHeaderCrc: 0);

    invalidData[8] ^= 0xFF;

    var reader = new SevenZipNextHeaderReader();

    var badRes = reader.Read(invalidData, out int badConsumed);
    Assert.Equal(SevenZipNextHeaderReadResult.InvalidData, badRes);
    Assert.Equal(invalidData.Length, badConsumed);

    reader.Reset();

    byte[] nextHeader = [0x10, 0x20, 0x30, 0x40];
    byte[] validFile = Build7zFile(
      nextHeaderOffset: 0,
      nextHeader);

    var goodRes = reader.Read(validFile, out int goodConsumed);

    Assert.Equal(SevenZipNextHeaderReadResult.Ok, goodRes);
    Assert.Equal(validFile.Length, goodConsumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal(nextHeader, reader.NextHeader.ToArray());
    Assert.Empty(reader.PackedStreams.ToArray());
  }

  private static byte[] BuildSignatureHeaderOnly(
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

  private static byte[] Build7zFile(
    ulong nextHeaderOffset,
    byte[] nextHeader)
  {
    ArgumentNullException.ThrowIfNull(nextHeader);

    var header = new SevenZipSignatureHeader(
      nextHeaderOffset,
      (ulong)nextHeader.Length,
      Crc32.Compute(nextHeader));

    byte[] file = new byte[SevenZipSignatureHeader.Size + (int)nextHeaderOffset + nextHeader.Length];
    header.Write(file.AsSpan(0, SevenZipSignatureHeader.Size));

    for (int i = 0; i < (int)nextHeaderOffset; i++)
      file[SevenZipSignatureHeader.Size + i] = 0xCC;

    nextHeader.CopyTo(file, SevenZipSignatureHeader.Size + (int)nextHeaderOffset);
    return file;
  }
}
