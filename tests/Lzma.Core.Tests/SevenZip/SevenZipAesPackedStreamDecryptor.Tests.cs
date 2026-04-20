using System.Security.Cryptography;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesPackedStreamDecryptorTests
{
  [Fact]
  public void TryDecrypt_ДляDirectKeyСНулевымКлючомИНулевымIv_РасшифровываетОдинБлок()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: SevenZipAesCoder.DirectKeyNumCyclesPower,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    // AES-256-CBC:
    // key = 32 нулевых байта,
    // iv = 16 нулевых байт,
    // plaintext = 16 нулевых байт.
    byte[] ciphertext = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    SevenZipAesDecryptResult result = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties,
        password,
        ciphertext,
        out byte[] plaintext);

    Assert.Equal(SevenZipAesDecryptResult.Ok, result);
    Assert.Equal(new byte[SevenZipAesDecryptor.AesBlockSize], plaintext);
  }

  [Fact]
  public void TryDecrypt_ДляSha256DerivedKeyИShortIv_РасшифровываетДанные()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 1,
        salt: [0xA1],
        initializationVector: [0xB1, 0xB2, 0xB3]);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    byte[] expectedPlaintext = Convert.FromHexString(
        "00112233445566778899AABBCCDDEEFF"
      + "102132435465768798A9BACBDCEDFE0F");

    byte[] ciphertext = EncryptForTest(
        properties,
        password,
        expectedPlaintext);

    SevenZipAesDecryptResult result = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties,
        password,
        ciphertext,
        out byte[] plaintext);

    Assert.Equal(SevenZipAesDecryptResult.Ok, result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecrypt_ДляНеподдерживаемогоNumCyclesPower_ВозвращаетNotSupported()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 25,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    SevenZipAesDecryptResult result = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties,
        password,
        ciphertext: new byte[SevenZipAesDecryptor.AesBlockSize],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipAesDecryptResult.NotSupported, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_ПриСлишкомДлинномIv_ВозвращаетInvalidData()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: new byte[SevenZipAesDecryptor.AesBlockSize + 1]);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    SevenZipAesDecryptResult result = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties,
        password,
        ciphertext: new byte[SevenZipAesDecryptor.AesBlockSize],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipAesDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_ЕслиCiphertextНеКратенБлоку_ВозвращаетInvalidData()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    using SevenZipPassword password = SevenZipPassword.FromString("p");

    SevenZipAesDecryptResult result = SevenZipAesPackedStreamDecryptor.TryDecrypt(
        properties,
        password,
        ciphertext: new byte[SevenZipAesDecryptor.AesBlockSize + 1],
        plaintext: out byte[] plaintext);

    Assert.Equal(SevenZipAesDecryptResult.InvalidData, result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecrypt_ПриNullProperties_БросаетArgumentNullException()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("p");

    Assert.Throws<ArgumentNullException>(
        () => SevenZipAesPackedStreamDecryptor.TryDecrypt(
            null!,
            password,
            ciphertext: [],
            plaintext: out _));
  }

  [Fact]
  public void TryDecrypt_ПриNullPassword_БросаетArgumentNullException()
  {
    var properties = new SevenZipAesProperties(
        numCyclesPower: 0,
        salt: [],
        initializationVector: []);

    Assert.Throws<ArgumentNullException>(
        () => SevenZipAesPackedStreamDecryptor.TryDecrypt(
            properties,
            null!,
            ciphertext: [],
            plaintext: out _));
  }

  private static byte[] EncryptForTest(
      SevenZipAesProperties properties,
      SevenZipPassword password,
      byte[] plaintext)
  {
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    byte[] iv = new byte[SevenZipAesDecryptor.AesBlockSize];

    try
    {
      Assert.True(SevenZipAesKeyDerivation.TryDeriveKey(
          properties,
          password,
          key));

      Assert.True(SevenZipAesInitializationVector.TryBuild(
          properties,
          iv));

      using Aes aes = Aes.Create();

      aes.KeySize = 256;
      aes.BlockSize = 128;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.None;

      using ICryptoTransform encryptor = aes.CreateEncryptor(
          key,
          iv);

      return encryptor.TransformFinalBlock(
          plaintext,
          0,
          plaintext.Length);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
      CryptographicOperations.ZeroMemory(iv);
    }
  }
}
