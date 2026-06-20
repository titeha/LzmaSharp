using Lzma.Core.Crypto.Gost;
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

  // ---- Парольный KDF через Стрибог ----

  // Собирает один блок KDF: соль || пароль(UTF-16LE) || счётчик(8 байт LE).
  private static byte[] BuildKdfBlock(byte[] salt, byte[] passwordUtf16Le, ulong counter)
  {
    byte[] block = new byte[salt.Length + passwordUtf16Le.Length + 8];
    salt.CopyTo(block, 0);
    passwordUtf16Le.CopyTo(block, salt.Length);
    System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(
        block.AsSpan(block.Length - 8, 8), counter);
    return block;
  }

  [Fact]
  public void TryDeriveStribogKey_ОдинРаунд_РавенHash256ОтОдногоБлока()
  {
    byte[] salt = [0xA1, 0xA2];
    byte[] passwordUtf16Le = [0x61, 0x00, 0x62, 0x00]; // "ab"

    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 0, // 2^0 = 1 раунд
        salt: salt,
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));

    byte[] expected = GostStribog.Hash256(BuildKdfBlock(salt, passwordUtf16Le, 0))
        .AsSpan(0, SevenZipGostKeyDerivation.Gost256KeySize).ToArray();

    Assert.Equal(expected, key);
  }

  [Fact]
  public void TryDeriveStribogKey_ДваРаунда_РавенHash256ОтКонкатенацииДвухБлоков()
  {
    byte[] salt = [0x11, 0x22, 0x33];
    byte[] passwordUtf16Le = [0x70, 0x00]; // "p"

    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 1, // 2^1 = 2 раунда
        salt: salt,
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));

    byte[] block0 = BuildKdfBlock(salt, passwordUtf16Le, 0);
    byte[] block1 = BuildKdfBlock(salt, passwordUtf16Le, 1);
    byte[] message = [.. block0, .. block1];

    byte[] expected = GostStribog.Hash256(message)
        .AsSpan(0, SevenZipGostKeyDerivation.Gost256KeySize).ToArray();

    Assert.Equal(expected, key);
  }

  [Fact]
  public void TryDeriveStribogKey_ДляDirectKey_ВозвращаетFalse()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0x01],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.False(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));
  }

  [Fact]
  public void TryDeriveStribogKey_ПриСлишкомБольшомNumCyclesPower_ВозвращаетFalse()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: (byte)(SevenZipGostCoder.SupportedNumCyclesPowerMax + 1),
        salt: [0x01],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.False(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));
  }

  [Fact]
  public void TryDeriveStribogKey_РазнаяСоль_ДаётРазныеКлючи()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] keyA = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    byte[] keyB = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(
        new SevenZipGostProperties(SevenZipGostCoder.CurrentPropertiesVersion, 0, 3, [0xAA], []),
        password,
        keyA));
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(
        new SevenZipGostProperties(SevenZipGostCoder.CurrentPropertiesVersion, 0, 3, [0xBB], []),
        password,
        keyB));

    Assert.NotEqual(keyA, keyB);
  }
}
