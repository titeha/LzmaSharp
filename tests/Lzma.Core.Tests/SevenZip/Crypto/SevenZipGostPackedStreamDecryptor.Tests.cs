using System.Text;

using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipGostPackedStreamDecryptorTests
{
  [Fact]
  public void TryBuildKuznyechikCtr_ПриКорректномIv_ВозвращаетIv()
  {
    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    bool result = SevenZipGostInitializationVector.TryBuildKuznyechikCtr(
        properties,
        out byte[] iv);

    Assert.True(result);
    Assert.Equal(
        new byte[] { 0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0 },
        iv);
  }

  [Fact]
  public void TryBuildKuznyechikCtr_ПриНекорректнойДлинеIv_ВозвращаетFalse()
  {
    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56]);

    bool result = SevenZipGostInitializationVector.TryBuildKuznyechikCtr(
        properties,
        out byte[] iv);

    Assert.False(result);
    Assert.Empty(iv);
  }

  [Fact]
  public void TryBuildKuznyechikCtr_ПриМаломБуфере_БросаетArgumentException()
  {
    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    Assert.Throws<ArgumentException>(
        () => SevenZipGostInitializationVector.TryBuildKuznyechikCtr(
            properties,
            new byte[7]));
  }

  [Fact]
  public void TryDecrypt_КузнечикПоОфициальномуCtrВектору_ВозвращаетИсходныйPlaintext()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] ciphertext = Convert.FromHexString(
        "F195D8BEC10ED1DBD57B5FA240BDA1B8"
      + "85EEE733F6A13E5DF33CE4B33C45DEE4"
      + "A5EAE88BE6356ED3D5E877F13564A3A5"
      + "CB91FAB1F20CBAB6D1C6D15820BDBA73");

    byte[] expectedPlaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988"
      + "00112233445566778899AABBCCEEFF0A"
      + "112233445566778899AABBCCEEFF0A00"
      + "2233445566778899AABBCCEEFF0A0011");

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикСНекорректнойДлинойIv_ВозвращаетInvalidData()
  {
    byte[] key = new byte[32];
    byte[] ciphertext = new byte[16];

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56]);

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикСНекорректнойДлинойКлюча_ВозвращаетInvalidData()
  {
    byte[] key = new byte[31];
    byte[] ciphertext = new byte[16];

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_МагмаПоОфициальномуCtrВектору_ВозвращаетИсходныйPlaintext()
  {
    byte[] key = Convert.FromHexString(
        "ffeeddccbbaa99887766554433221100f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff");

    byte[] ciphertext = Convert.FromHexString(
        "4e98110c97b7b93c3e250d93d6e85d69136d868807b2dbef568eb680ab52a12d");

    byte[] expectedPlaintext = Convert.FromHexString(
        "92def06b3c130a59db54c704f8189d204a98fb2e67a8024c8912409b17b57e41");

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78]);

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.MagmaMethodId,
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecrypt_МагмаСНекорректнойДлинойIv_ВозвращаетInvalidData()
  {
    byte[] key = new byte[32];
    byte[] ciphertext = new byte[8];

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56]); // должно быть 4 байта

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.MagmaMethodId,
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_НеизвестныйMethodId_ВозвращаетInvalidData()
  {
    byte[] key = new byte[32];
    byte[] ciphertext = new byte[16];

    var properties = new SevenZipGostProperties(
        version: 1,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: [0x00],
        properties: properties,
        key: key,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикDirectKeyПоОфициальномуCtrВектору_ВозвращаетИсходныйPlaintext()
  {
    byte[] officialKey = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] ciphertext = Convert.FromHexString(
        "F195D8BEC10ED1DBD57B5FA240BDA1B8"
      + "85EEE733F6A13E5DF33CE4B33C45DEE4"
      + "A5EAE88BE6356ED3D5E877F13564A3A5"
      + "CB91FAB1F20CBAB6D1C6D15820BDBA73");

    byte[] expectedPlaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988"
      + "00112233445566778899AABBCCEEFF0A"
      + "112233445566778899AABBCCEEFF0A00"
      + "2233445566778899AABBCCEEFF0A0011");

    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: officialKey,
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ignored");

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: password,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикDirectKeyССольюИПаролем_ДелаетRoundtrip()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0xA1, 0xA2],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      Assert.True(SevenZipGostKeyDerivation.TryDeriveDirectKey(
          properties,
          password,
          key));

      byte[] plaintext = System.Text.Encoding.UTF8.GetBytes(
          "LzmaSharp GOST Kuznyechik direct key test");

      Assert.True(GostKuznyechikCtrTransform.TryTransform(
          key,
          properties.InitializationVector,
          plaintext,
          out byte[] ciphertext));

      SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
          methodId: SevenZipGostCoder.KuznyechikMethodId,
          properties: properties,
          password: password,
          ciphertext: ciphertext,
          plaintext: out byte[] decoded);

      Assert.Equal(SevenZipGostDecryptResult.Ok, result);
      Assert.Equal(plaintext, decoded);
    }
    finally
    {
      Array.Clear(key);
    }
  }

  [Fact]
  public void TryDecrypt_КузнечикStribogKdfБезСоли_ДелаетRoundtrip()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 3,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");

    byte[] plain = Encoding.ASCII.GetBytes("LzmaSharp GOST Stribog KDF no-salt round-trip");

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildKuznyechikCtr(properties, out byte[] iv));
    Assert.True(GostKuznyechikCtrTransform.TryTransform(key, iv, plain, out byte[] ciphertext));

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: password,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(plain, plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикDirectKeyСНекорректнымIv_ВозвращаетInvalidData()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: password,
        ciphertext: new byte[16],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_МагмаDirectKeyССольюИПаролем_ДелаетRoundtrip()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0xA1, 0xA2],
        initializationVector: [0x12, 0x34, 0x56, 0x78]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] plain = Encoding.ASCII.GetBytes("LzmaSharp GOST Magma direct key test");

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveDirectKey(properties, password, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildMagmaCtr(properties, out byte[] iv));
    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, plain, out byte[] ciphertext));

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.MagmaMethodId,
        properties: properties,
        password: password,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(plain, plaintext);
  }

  [Fact]
  public void TryDecrypt_КузнечикStribogKdfССольюИПаролем_ДелаетRoundtrip()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 4, // 2^4 = 16 раундов — парольный KDF, не direct-key
        salt: [0xA1, 0xA2, 0xA3, 0xA4],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("пароль");

    byte[] plain = Encoding.ASCII.GetBytes("LzmaSharp GOST Kuznyechik Stribog KDF round-trip");

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildKuznyechikCtr(properties, out byte[] iv));
    Assert.True(GostKuznyechikCtrTransform.TryTransform(key, iv, plain, out byte[] ciphertext));

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: password,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(plain, plaintext);
  }

  [Fact]
  public void TryDecrypt_МагмаStribogKdfССольюИПаролем_ДелаетRoundtrip()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 5, // 2^5 = 32 раунда — парольный KDF
        salt: [0xB1, 0xB2],
        initializationVector: [0x10, 0x32, 0x54, 0x76]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    byte[] plain = Encoding.ASCII.GetBytes("LzmaSharp GOST Magma Stribog KDF round-trip");

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildMagmaCtr(properties, out byte[] iv));
    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, plain, out byte[] ciphertext));

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.MagmaMethodId,
        properties: properties,
        password: password,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.Equal(plain, plaintext);
  }

  [Fact]
  public void TryDecrypt_НеверныйПарольСStribogKdf_ДаётДругойPlaintext()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 4,
        salt: [0xA1, 0xA2, 0xA3, 0xA4],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    byte[] plain = Encoding.ASCII.GetBytes("LzmaSharp GOST wrong password test");

    using SevenZipPassword right = SevenZipPassword.FromString("правильный");
    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];
    Assert.True(SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, right, key));
    Assert.True(SevenZipGostInitializationVector.TryBuildKuznyechikCtr(properties, out byte[] iv));
    Assert.True(GostKuznyechikCtrTransform.TryTransform(key, iv, plain, out byte[] ciphertext));

    using SevenZipPassword wrong = SevenZipPassword.FromString("неверный");
    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: wrong,
        ciphertext: ciphertext,
        plaintext: out byte[] plaintext);

    // На уровне CTR неверный пароль не обнаруживается — он просто даёт другой
    // открытый текст (несовпадение поймает CRC на уровне фолдера/архива).
    Assert.Equal(SevenZipGostDecryptResult.Ok, result);
    Assert.NotEqual(plain, plaintext);
  }

  [Fact]
  public void TryDecrypt_СлишкомБольшойNumCyclesPowerСПаролем_ВозвращаетNotSupported()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: (byte)(SevenZipGostCoder.SupportedNumCyclesPowerMax + 1),
        salt: [0xA1],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: SevenZipGostCoder.KuznyechikMethodId,
        properties: properties,
        password: password,
        ciphertext: new byte[16],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.NotSupported, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_НеизвестныйMethodIdСПаролем_ВозвращаетInvalidData()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipGostDecryptResult result = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        methodId: [0x00],
        properties: properties,
        password: password,
        ciphertext: new byte[16],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipGostDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_ПриNullPropertiesСПаролем_БросаетArgumentNullException()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("");

    Assert.Throws<ArgumentNullException>(
        () => SevenZipGostPackedStreamDecryptor.TryDecrypt(
            methodId: SevenZipGostCoder.KuznyechikMethodId,
            properties: null!,
            password: password,
            ciphertext: [],
            plaintext: out _));
  }

  [Fact]
  public void TryDecrypt_ПриNullPassword_БросаетArgumentNullException()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    Assert.Throws<ArgumentNullException>(
        () => SevenZipGostPackedStreamDecryptor.TryDecrypt(
            methodId: SevenZipGostCoder.KuznyechikMethodId,
            properties: properties,
            password: null!,
            ciphertext: [],
            plaintext: out _));
  }
}
