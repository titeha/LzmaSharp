using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesKeyDerivationTests
{
  [Fact]
  public void TryDeriveDirectKey_Для0x3F_КладетSaltПотомUtf16LeПарольПотомНули()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [0xA1, 0xA2],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    byte[] expected = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    expected[0] = 0xA1;
    expected[1] = 0xA2;
    expected[2] = 0x61;
    expected[3] = 0x00;
    expected[4] = 0x62;
    expected[5] = 0x00;

    Assert.Equal(expected, key);
  }

  [Fact]
  public void TryDeriveDirectKey_Для0x3FИПустогоПароля_КладетSaltПотомНули()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [0x10, 0x20, 0x30],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    byte[] expected = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    expected[0] = 0x10;
    expected[1] = 0x20;
    expected[2] = 0x30;

    Assert.Equal(expected, key);
  }

  [Fact]
  public void TryDeriveDirectKey_ЕслиПарольДлиннееОстаткаКлюча_ОбрезаетДо32Байт()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [0xAA],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("12345678901234567890");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    Assert.Equal(0xAA, key[0]);

    byte[] passwordBytes = password.ToUtf16LeByteArray();
    Assert.Equal(
        passwordBytes.AsSpan(0, SevenZipAesKeyDerivation.Aes256KeySize - 1).ToArray(),
        key.AsSpan(1).ToArray());
  }

  [Fact]
  public void TryDeriveDirectKey_ДляОбычногоNumCyclesPower_ВозвращаетFalseИОчищаетDestination()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 19,
        salt: [0xA1, 0xA2],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = Enumerable.Repeat((byte)0xCC, SevenZipAesKeyDerivation.Aes256KeySize).ToArray();

    bool result = SevenZipAesKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.False(result);
    Assert.Equal(new byte[SevenZipAesKeyDerivation.Aes256KeySize], key);
  }

  [Fact]
  public void TryDeriveDirectKey_ПриМаломБуфере_БросаетArgumentException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    Assert.Throws<ArgumentException>(
        () => SevenZipAesKeyDerivation.TryDeriveDirectKey(
            properties,
            password,
            new byte[SevenZipAesKeyDerivation.Aes256KeySize - 1]));
  }

  [Fact]
  public void TryDeriveDirectKey_ПослеDisposeПароля_БросаетObjectDisposedException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    SevenZipPassword password = SevenZipPassword.FromString("ab");
    password.Dispose();

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    Assert.Throws<ObjectDisposedException>(
        () => SevenZipAesKeyDerivation.TryDeriveDirectKey(
            properties,
            password,
            key));
  }
}
