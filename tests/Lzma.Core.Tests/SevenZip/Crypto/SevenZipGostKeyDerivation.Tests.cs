using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipGostKeyDerivationTests
{
  [Fact]
  public void TryDeriveDirectKey_Для0x3F_КладетSaltПотомUtf16LeПарольПотомНули()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0xA1, 0xA2],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    byte[] expected = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
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
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0x10, 0x20, 0x30],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    byte[] expected = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    expected[0] = 0x10;
    expected[1] = 0x20;
    expected[2] = 0x30;

    Assert.Equal(expected, key);
  }

  [Fact]
  public void TryDeriveDirectKey_ЕслиПарольДлиннееОстаткаКлюча_ОбрезаетДо32Байт()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0xAA],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("12345678901234567890");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);

    Assert.Equal(0xAA, key[0]);

    byte[] passwordBytes = password.ToUtf16LeByteArray();

    Assert.Equal(
        passwordBytes.AsSpan(0, SevenZipGostKeyDerivation.Gost256KeySize - 1).ToArray(),
        key.AsSpan(1).ToArray());
  }

  [Fact]
  public void TryDeriveDirectKey_ЕслиSaltЗанимаетВесьКлюч_ПарольИгнорируется()
  {
    byte[] salt = Enumerable.Range(0, SevenZipGostKeyDerivation.Gost256KeySize)
        .Select(i => (byte)i)
        .ToArray();

    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: salt,
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ignored");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.True(result);
    Assert.Equal(salt, key);
  }

  [Fact]
  public void TryDeriveDirectKey_ДляОбычногоNumCyclesPower_ВозвращаетFalseИОчищаетDestination()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 3,
        salt: [0xA1, 0xA2],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = Enumerable.Repeat((byte)0xCC, SevenZipGostKeyDerivation.Gost256KeySize).ToArray();

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.False(result);
    Assert.Equal(new byte[SevenZipGostKeyDerivation.Gost256KeySize], key);
  }

  [Fact]
  public void TryDeriveDirectKey_ПриSaltБольшеКлюча_ВозвращаетFalse()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: new byte[SevenZipGostKeyDerivation.Gost256KeySize + 1],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    bool result = SevenZipGostKeyDerivation.TryDeriveDirectKey(
        properties,
        password,
        key);

    Assert.False(result);
    Assert.Equal(new byte[SevenZipGostKeyDerivation.Gost256KeySize], key);
  }

  [Fact]
  public void TryDeriveDirectKey_ПриМаломБуфере_БросаетArgumentException()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    Assert.Throws<ArgumentException>(
        () => SevenZipGostKeyDerivation.TryDeriveDirectKey(
            properties,
            password,
            new byte[SevenZipGostKeyDerivation.Gost256KeySize - 1]));
  }

  [Fact]
  public void TryDeriveDirectKey_ПослеDisposeПароля_БросаетObjectDisposedException()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    SevenZipPassword password = SevenZipPassword.FromString("ab");
    password.Dispose();

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    Assert.Throws<ObjectDisposedException>(
        () => SevenZipGostKeyDerivation.TryDeriveDirectKey(
            properties,
            password,
            key));
  }

  [Fact]
  public void TryDeriveDirectKey_ПриNullProperties_БросаетArgumentNullException()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    Assert.Throws<ArgumentNullException>(
        () => SevenZipGostKeyDerivation.TryDeriveDirectKey(
            null!,
            password,
            new byte[SevenZipGostKeyDerivation.Gost256KeySize]));
  }

  [Fact]
  public void TryDeriveDirectKey_ПриNullPassword_БросаетArgumentNullException()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    Assert.Throws<ArgumentNullException>(
        () => SevenZipGostKeyDerivation.TryDeriveDirectKey(
            properties,
            null!,
            new byte[SevenZipGostKeyDerivation.Gost256KeySize]));
  }
}
