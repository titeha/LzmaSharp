using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesInitializationVectorTests
{
  [Fact]
  public void TryBuild_ПустойIv_Возвращает16НулевыхБайт()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: []);

    bool result = SevenZipAesInitializationVector.TryBuild(
        properties,
        out byte[] iv);

    Assert.True(result);
    Assert.Equal(new byte[SevenZipAesDecryptor.AesBlockSize], iv);
  }

  [Fact]
  public void TryBuild_КороткийIv_ДополняетНулямиДо16Байт()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: [0xA1, 0xA2, 0xA3]);

    bool result = SevenZipAesInitializationVector.TryBuild(
        properties,
        out byte[] iv);

    Assert.True(result);

    byte[] expected = new byte[SevenZipAesDecryptor.AesBlockSize];
    expected[0] = 0xA1;
    expected[1] = 0xA2;
    expected[2] = 0xA3;

    Assert.Equal(expected, iv);
  }

  [Fact]
  public void TryBuild_ПолныйIv_ВозвращаетЕгоБезИзменений()
  {
    byte[] source =
    [
      0x00, 0x01, 0x02, 0x03,
      0x04, 0x05, 0x06, 0x07,
      0x08, 0x09, 0x0A, 0x0B,
      0x0C, 0x0D, 0x0E, 0x0F,
    ];

    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: source);

    bool result = SevenZipAesInitializationVector.TryBuild(
        properties,
        out byte[] iv);

    Assert.True(result);
    Assert.Equal(source, iv);
  }

  [Fact]
  public void TryBuild_СлишкомДлинныйIv_ВозвращаетFalse()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: new byte[SevenZipAesDecryptor.AesBlockSize + 1]);

    bool result = SevenZipAesInitializationVector.TryBuild(
        properties,
        out byte[] iv);

    Assert.False(result);
    Assert.Empty(iv);
  }

  [Fact]
  public void TryBuild_ВSpan_ОчищаетТолькоПервые16БайтИОставляетХвостБуфера()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: [0xAA, 0xBB]);

    byte[] destination = Enumerable.Repeat((byte)0xCC, 20).ToArray();

    bool result = SevenZipAesInitializationVector.TryBuild(
        properties,
        destination);

    Assert.True(result);

    byte[] expectedPrefix = new byte[SevenZipAesDecryptor.AesBlockSize];
    expectedPrefix[0] = 0xAA;
    expectedPrefix[1] = 0xBB;

    Assert.Equal(expectedPrefix, destination.AsSpan(0, SevenZipAesDecryptor.AesBlockSize).ToArray());
    Assert.Equal(new byte[] { 0xCC, 0xCC, 0xCC, 0xCC }, destination.AsSpan(16).ToArray());
  }

  [Fact]
  public void TryBuild_ПриМаломБуфере_БросаетArgumentException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [],
        initializationVector: []);

    Assert.Throws<ArgumentException>(
        () => SevenZipAesInitializationVector.TryBuild(
            properties,
            new byte[SevenZipAesDecryptor.AesBlockSize - 1]));
  }

  [Fact]
  public void TryBuild_ПриNullProperties_БросаетArgumentNullException()
  {
    Assert.Throws<ArgumentNullException>(
        () => SevenZipAesInitializationVector.TryBuild(
            null!,
            new byte[SevenZipAesDecryptor.AesBlockSize]));
  }
}
