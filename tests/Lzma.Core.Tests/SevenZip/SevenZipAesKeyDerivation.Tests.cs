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

  [Fact]
  public void TryDeriveSha256Key_Для0БезSaltИПароля_ВозвращаетОжидаемыйКлюч()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.True(result);
    Assert.Equal(
        Convert.FromHexString("AF5570F5A1810B7AF78CAF4BC70A660F0DF51E42BAF91D4DE5B2328DE0E83DFC"),
        key);
  }

  [Fact]
  public void TryDeriveSha256Key_Для0ССольюИПаролем_ВозвращаетОжидаемыйКлюч()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [0xA1, 0xA2],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.True(result);
    Assert.Equal(
        Convert.FromHexString("4D8F2072CABB39C1D64A4C05B3F388AF89C78795D3F23A9ECD87CC896C9FCCB9"),
        key);
  }

  [Fact]
  public void TryDeriveSha256Key_Для1ССольюИПаролем_ВозвращаетОжидаемыйКлюч()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 1,
        salt: [0xA1],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.True(result);
    Assert.Equal(
        Convert.FromHexString("BE63EDB4A17DD1D8DC9975E3B869D516410C113BE644057978824970BE774E6A"),
        key);
  }

  [Fact]
  public void TryDeriveSha256Key_Для2СКириллическимПаролем_ВозвращаетОжидаемыйКлюч()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 2,
        salt: [0x01, 0x02, 0x03],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("пар");

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.True(result);
    Assert.Equal(
        Convert.FromHexString("190CF6A2C7A831B3AAFAE2E676961734AEAE22529AAE0871D38345B84BE8B225"),
        key);
  }

  [Fact]
  public void TryDeriveSha256Key_ДляDirectNumCyclesPower_ВозвращаетFalseИОчищаетDestination()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [0xA1],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = Enumerable.Repeat((byte)0xCC, SevenZipAesKeyDerivation.Aes256KeySize).ToArray();

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.False(result);
    Assert.Equal(new byte[SevenZipAesKeyDerivation.Aes256KeySize], key);
  }

  [Fact]
  public void TryDeriveSha256Key_ДляНеподдерживаемогоNumCyclesPower_ВозвращаетFalseИОчищаетDestination()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 25,
        salt: [0xA1],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = Enumerable.Repeat((byte)0xCC, SevenZipAesKeyDerivation.Aes256KeySize).ToArray();

    bool result = SevenZipAesKeyDerivation.TryDeriveSha256Key(
        properties,
        password,
        key);

    Assert.False(result);
    Assert.Equal(new byte[SevenZipAesKeyDerivation.Aes256KeySize], key);
  }

  [Fact]
  public void TryDeriveSha256Key_ПриМаломБуфере_БросаетArgumentException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    Assert.Throws<ArgumentException>(
        () => SevenZipAesKeyDerivation.TryDeriveSha256Key(
            properties,
            password,
            new byte[SevenZipAesKeyDerivation.Aes256KeySize - 1]));
  }

  [Fact]
  public void TryDeriveSha256Key_ПослеDisposeПароля_БросаетObjectDisposedException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    SevenZipPassword password = SevenZipPassword.FromString("ab");
    password.Dispose();

    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];

    Assert.Throws<ObjectDisposedException>(
        () => SevenZipAesKeyDerivation.TryDeriveSha256Key(
            properties,
            password,
            key));
  }
}
