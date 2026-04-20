using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesCoderTests
{
  [Fact]
  public void IsAesMethodId_Для7zAesMethodId_ВозвращаетTrue()
  {
    Assert.True(SevenZipAesCoder.IsAesMethodId([0x06, 0xF1, 0x07, 0x01]));
  }

  [Theory]
  [InlineData(new byte[] { })]
  [InlineData(new byte[] { 0x06 })]
  [InlineData(new byte[] { 0x06, 0xF1, 0x07 })]
  [InlineData(new byte[] { 0x06, 0xF1, 0x07, 0x00 })]
  [InlineData(new byte[] { 0x21 })]
  [InlineData(new byte[] { 0x03, 0x01, 0x01 })]
  public void IsAesMethodId_ДляДругихMethodId_ВозвращаетFalse(byte[] methodId)
  {
    Assert.False(SevenZipAesCoder.IsAesMethodId(methodId));
  }

  [Fact]
  public void TryParseProperties_ПустыеProperties_ВозвращаетЗначенияПоУмолчанию()
  {
    Assert.True(SevenZipAesCoder.TryParseProperties([], out SevenZipAesProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(0, parsed!.NumCyclesPower);
    Assert.Empty(parsed.Salt);
    Assert.Empty(parsed.InitializationVector);
  }

  [Fact]
  public void TryParseProperties_ОдинБайтБезSaltИIv_ВозвращаетNumCyclesPower()
  {
    Assert.True(SevenZipAesCoder.TryParseProperties([0x13], out SevenZipAesProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(0x13, parsed!.NumCyclesPower);
    Assert.Empty(parsed.Salt);
    Assert.Empty(parsed.InitializationVector);
  }

  [Fact]
  public void TryParseProperties_ОдинБайтБезSaltИIvНоСЛишнимБайтом_ВозвращаетFalse()
  {
    Assert.False(SevenZipAesCoder.TryParseProperties([0x13, 0x00], out SevenZipAesProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_СSaltИIv_ВозвращаетРазобранныеЗначения()
  {
    // b0:
    //   младшие 6 бит = NumCyclesPower = 19;
    //   bit7 = есть salt;
    //   bit6 = есть IV.
    //
    // b1:
    //   high nibble = 1 => saltSize = 1 + 1 = 2;
    //   low nibble  = 2 => ivSize   = 1 + 2 = 3.
    byte[] properties =
    [
      0xD3,
    0x12,
    0xA1, 0xA2,
    0xB1, 0xB2, 0xB3,
  ];

    Assert.True(SevenZipAesCoder.TryParseProperties(properties, out SevenZipAesProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(19, parsed!.NumCyclesPower);
    Assert.Equal(new byte[] { 0xA1, 0xA2 }, parsed.Salt);
    Assert.Equal(new byte[] { 0xB1, 0xB2, 0xB3 }, parsed.InitializationVector);
  }

  [Fact]
  public void TryParseProperties_ЕстьФлагиSaltИIvНоНетВторогоБайта_ВозвращаетFalse()
  {
    Assert.False(SevenZipAesCoder.TryParseProperties([0xD3], out SevenZipAesProperties? parsed));

    Assert.Null(parsed);
  }

  [Theory]
  [InlineData(new byte[] { 0xD3, 0x12, 0xA1, 0xA2, 0xB1, 0xB2 })]
  [InlineData(new byte[] { 0xD3, 0x12, 0xA1, 0xA2, 0xB1, 0xB2, 0xB3, 0x00 })]
  public void TryParseProperties_РазмерНеСовпадаетСSaltИIv_ВозвращаетFalse(byte[] properties)
  {
    Assert.False(SevenZipAesCoder.TryParseProperties(properties, out SevenZipAesProperties? parsed));

    Assert.Null(parsed);
  }

  [Theory]
  [InlineData((byte)0)]
  [InlineData((byte)19)]
  [InlineData((byte)24)]
  [InlineData((byte)0x3F)]
  public void IsSupportedNumCyclesPower_ДляПоддерживаемыхЗначений_ВозвращаетTrue(byte numCyclesPower)
  {
    Assert.True(SevenZipAesCoder.IsSupportedNumCyclesPower(numCyclesPower));
  }

  [Theory]
  [InlineData((byte)25)]
  [InlineData((byte)62)]
  public void IsSupportedNumCyclesPower_ДляНеподдерживаемыхЗначений_ВозвращаетFalse(byte numCyclesPower)
  {
    Assert.False(SevenZipAesCoder.IsSupportedNumCyclesPower(numCyclesPower));
  }
}
