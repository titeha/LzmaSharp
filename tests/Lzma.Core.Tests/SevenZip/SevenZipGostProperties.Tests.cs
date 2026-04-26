using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipGostPropertiesTests
{
  [Fact]
  public void TryParseProperties_Версия1БезSaltИIv_ВозвращаетРазобранныеЗначения()
  {
    byte[] properties =
    [
      0x01, // version
      0x00, // flags
      0x13, // numCyclesPower
      0x00, // saltSize
      0x00, // ivSize
    ];

    Assert.True(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(1, parsed!.Version);
    Assert.Equal(0, parsed.Flags);
    Assert.Equal(0x13, parsed.NumCyclesPower);
    Assert.Empty(parsed.Salt);
    Assert.Empty(parsed.InitializationVector);
  }

  [Fact]
  public void TryParseProperties_Версия1СSaltИИv_ВозвращаетРазобранныеЗначения()
  {
    byte[] properties =
    [
      0x01, // version
      0x00, // flags
      0x03, // numCyclesPower
      0x02, // saltSize
      0x03, // ivSize
      0xA1, 0xA2,
      0xB1, 0xB2, 0xB3,
    ];

    Assert.True(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.NotNull(parsed);
    Assert.Equal(1, parsed!.Version);
    Assert.Equal(0, parsed.Flags);
    Assert.Equal(0x03, parsed.NumCyclesPower);
    Assert.Equal(new byte[] { 0xA1, 0xA2 }, parsed.Salt);
    Assert.Equal(new byte[] { 0xB1, 0xB2, 0xB3 }, parsed.InitializationVector);
  }

  [Fact]
  public void TryParseProperties_ПустыеProperties_ВозвращаетFalse()
  {
    Assert.False(SevenZipGostCoder.TryParseProperties(
        [],
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_СлишкомКороткиеProperties_ВозвращаетFalse()
  {
    Assert.False(SevenZipGostCoder.TryParseProperties(
        [0x01, 0x00, 0x00, 0x00],
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_НеизвестнаяВерсия_ВозвращаетFalse()
  {
    byte[] properties =
    [
      0x02, // version
      0x00,
      0x00,
      0x00,
      0x00,
    ];

    Assert.False(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_НенулевыеFlags_ВозвращаетFalse()
  {
    byte[] properties =
    [
      0x01, // version
      0x01, // flags
      0x00,
      0x00,
      0x00,
    ];

    Assert.False(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Theory]
  [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x02, 0x03, 0xA1, 0xA2, 0xB1, 0xB2 })]
  [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x02, 0x03, 0xA1, 0xA2, 0xB1, 0xB2, 0xB3, 0x00 })]
  public void TryParseProperties_НесовпадающийРазмер_ВозвращаетFalse(byte[] properties)
  {
    Assert.False(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_SaltБольшеМаксимума_ВозвращаетFalse()
  {
    byte[] properties =
    [
      SevenZipGostCoder.CurrentPropertiesVersion,
      0,
      0,
      (SevenZipGostCoder.MaxSaltSize + 1),
      0,
    ];

    Assert.False(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }

  [Fact]
  public void TryParseProperties_IvБольшеМаксимума_ВозвращаетFalse()
  {
    byte[] properties =
    [
      SevenZipGostCoder.CurrentPropertiesVersion,
      0,
      0,
      0,
      (SevenZipGostCoder.MaxInitializationVectorSize + 1),
    ];

    Assert.False(SevenZipGostCoder.TryParseProperties(
        properties,
        out SevenZipGostProperties? parsed));

    Assert.Null(parsed);
  }
}
