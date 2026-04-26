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
  public void TryDecrypt_МагмаПокаВозвращаетNotSupported()
  {
    byte[] key = new byte[32];
    byte[] ciphertext = new byte[16];

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

    Assert.Equal(SevenZipGostDecryptResult.NotSupported, result);
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
}
