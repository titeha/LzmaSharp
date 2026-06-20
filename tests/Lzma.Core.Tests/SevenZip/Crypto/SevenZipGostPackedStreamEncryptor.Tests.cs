using System.Text;

using Lzma.Core.Crypto.Gost;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipGostPackedStreamEncryptorTests
{
  // ---- Шифрование ↔ дешифрование (CTR симметричен) ----

  [Fact]
  public void TryEncrypt_КузнечикDirectKey_ДекриптуетсяОбратноВИсходныйPlaintext()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [0xA1, 0xA2],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");
    byte[] plain = Encoding.UTF8.GetBytes("LzmaSharp GOST Kuznyechik encryptor direct-key");

    SevenZipGostEncryptResult encryptResult = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        SevenZipGostCoder.KuznyechikMethodId, properties, password, plain, out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.Ok, encryptResult);
    Assert.NotEqual(plain, ciphertext);

    SevenZipGostDecryptResult decryptResult = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        SevenZipGostCoder.KuznyechikMethodId, properties, password, ciphertext, out byte[] decrypted);

    Assert.Equal(SevenZipGostDecryptResult.Ok, decryptResult);
    Assert.Equal(plain, decrypted);
  }

  [Fact]
  public void TryEncrypt_МагмаStribogKdf_ДекриптуетсяОбратноВИсходныйPlaintext()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 5,
        salt: [0xB1, 0xB2, 0xB3],
        initializationVector: [0x10, 0x32, 0x54, 0x76]);

    using SevenZipPassword password = SevenZipPassword.FromString("пароль");
    byte[] plain = Encoding.UTF8.GetBytes("LzmaSharp GOST Magma encryptor Stribog KDF round-trip");

    SevenZipGostEncryptResult encryptResult = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        SevenZipGostCoder.MagmaMethodId, properties, password, plain, out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.Ok, encryptResult);

    SevenZipGostDecryptResult decryptResult = SevenZipGostPackedStreamDecryptor.TryDecrypt(
        SevenZipGostCoder.MagmaMethodId, properties, password, ciphertext, out byte[] decrypted);

    Assert.Equal(SevenZipGostDecryptResult.Ok, decryptResult);
    Assert.Equal(plain, decrypted);
  }

  [Fact]
  public void TryEncrypt_ГотовымКлючом_СовпадаетСПрямымCtrПреобразованием()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    byte[] key = new byte[SevenZipGostKeyDerivation.Gost256KeySize];
    for (int i = 0; i < key.Length; i++)
      key[i] = unchecked((byte)(i * 7 + 1));

    byte[] plain = Encoding.UTF8.GetBytes("direct ctr comparison");

    SevenZipGostEncryptResult result = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        SevenZipGostCoder.KuznyechikMethodId, properties, key, plain, out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.Ok, result);

    Assert.True(GostKuznyechikCtrTransform.TryTransform(
        key, properties.InitializationVector, plain, out byte[] expected));
    Assert.Equal(expected, ciphertext);
  }

  [Fact]
  public void TryEncrypt_НеизвестныйMethodId_ВозвращаетInvalidData()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipGostEncryptResult result = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        [0x00], properties, password, [1, 2, 3], out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.InvalidData, result);
    Assert.Empty(ciphertext);
  }

  [Fact]
  public void TryEncrypt_СлишкомБольшойNumCyclesPower_ВозвращаетNotSupported()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: (byte)(SevenZipGostCoder.SupportedNumCyclesPowerMax + 1),
        salt: [0xA1],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    using SevenZipPassword password = SevenZipPassword.FromString("ab");

    SevenZipGostEncryptResult result = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        SevenZipGostCoder.KuznyechikMethodId, properties, password, [1, 2, 3], out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.NotSupported, result);
    Assert.Empty(ciphertext);
  }

  [Fact]
  public void TryEncrypt_КузнечикСНекорректнойДлинойIv_ВозвращаетInvalidData()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: SevenZipGostCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: [0x12, 0x34, 0x56]); // не 8 байт

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipGostEncryptResult result = SevenZipGostPackedStreamEncryptor.TryEncrypt(
        SevenZipGostCoder.KuznyechikMethodId, properties, password, new byte[16], out byte[] ciphertext);

    Assert.Equal(SevenZipGostEncryptResult.InvalidData, result);
    Assert.Empty(ciphertext);
  }

  // ---- Сериализация свойств ↔ парсинг ----

  [Fact]
  public void TrySerializeProperties_RoundTripСПарсингом()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 7,
        salt: [0xA1, 0xA2, 0xA3, 0xA4],
        initializationVector: [0x12, 0x34, 0x56, 0x78, 0x90, 0xAB, 0xCE, 0xF0]);

    Assert.True(SevenZipGostCoder.TrySerializeProperties(properties, out byte[] serialized));

    Assert.True(SevenZipGostCoder.TryParseProperties(serialized, out SevenZipGostProperties? parsed));
    Assert.NotNull(parsed);
    Assert.Equal(properties.Version, parsed!.Version);
    Assert.Equal(properties.Flags, parsed.Flags);
    Assert.Equal(properties.NumCyclesPower, parsed.NumCyclesPower);
    Assert.Equal(properties.Salt, parsed.Salt);
    Assert.Equal(properties.InitializationVector, parsed.InitializationVector);
  }

  [Fact]
  public void TrySerializeProperties_РаскладкаБайтСоответствуетФормату()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0,
        numCyclesPower: 0x05,
        salt: [0xAA, 0xBB],
        initializationVector: [0x01, 0x02, 0x03, 0x04]);

    Assert.True(SevenZipGostCoder.TrySerializeProperties(properties, out byte[] serialized));

    byte[] expected =
    [
      SevenZipGostCoder.CurrentPropertiesVersion, 0x00, 0x05, 0x02, 0x04,
      0xAA, 0xBB,
      0x01, 0x02, 0x03, 0x04,
    ];
    Assert.Equal(expected, serialized);
  }

  [Fact]
  public void TrySerializeProperties_НеверныйFlags_ВозвращаетFalse()
  {
    var properties = new SevenZipGostProperties(
        version: SevenZipGostCoder.CurrentPropertiesVersion,
        flags: 0x01,
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    Assert.False(SevenZipGostCoder.TrySerializeProperties(properties, out byte[] serialized));
    Assert.Empty(serialized);
  }
}
