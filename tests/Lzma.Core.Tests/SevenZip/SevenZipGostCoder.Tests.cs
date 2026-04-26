using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipGostCoderTests
{
  [Fact]
  public void IsKuznyechikMethodId_ДляСвоегоMethodId_ВозвращаетTrue()
  {
    Assert.True(SevenZipGostCoder.IsKuznyechikMethodId(
        [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x01]));
  }

  [Fact]
  public void IsMagmaMethodId_ДляСвоегоMethodId_ВозвращаетTrue()
  {
    Assert.True(SevenZipGostCoder.IsMagmaMethodId(
        [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x02]));
  }

  [Fact]
  public void IsGostMethodId_ДляКузнечика_ВозвращаетTrue()
  {
    Assert.True(SevenZipGostCoder.IsGostMethodId(
        [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x01]));
  }

  [Fact]
  public void IsGostMethodId_ДляМагмы_ВозвращаетTrue()
  {
    Assert.True(SevenZipGostCoder.IsGostMethodId(
        [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x02]));
  }

  [Theory]
  [InlineData(new byte[] { })]
  [InlineData(new byte[] { 0x3F })]
  [InlineData(new byte[] { 0x06, 0xF1, 0x07, 0x01 })]
  [InlineData(new byte[] { 0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x03 })]
  [InlineData(new byte[] { 0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00 })]
  public void IsGostMethodId_ДляДругихMethodId_ВозвращаетFalse(byte[] methodId)
  {
    Assert.False(SevenZipGostCoder.IsGostMethodId(methodId));
    Assert.False(SevenZipGostCoder.IsKuznyechikMethodId(methodId));
    Assert.False(SevenZipGostCoder.IsMagmaMethodId(methodId));
  }
}
