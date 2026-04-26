using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostKuznyechikCipherTests
{
  [Fact]
  public void TryEncryptBlock_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйCiphertext()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] plaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988");

    byte[] expectedCiphertext = Convert.FromHexString(
        "7F679D90BEBC24305A468D42B9D4EDCD");

    byte[] ciphertext = new byte[GostKuznyechikCipher.BlockSize];

    bool result = GostKuznyechikCipher.TryEncryptBlock(
        key,
        plaintext,
        ciphertext);

    Assert.True(result);
    Assert.Equal(expectedCiphertext, ciphertext);
  }

  [Fact]
  public void TryDecryptBlock_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйPlaintext()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] ciphertext = Convert.FromHexString(
        "7F679D90BEBC24305A468D42B9D4EDCD");

    byte[] expectedPlaintext = Convert.FromHexString(
        "1122334455667700FFEEDDCCBBAA9988");

    byte[] plaintext = new byte[GostKuznyechikCipher.BlockSize];

    bool result = GostKuznyechikCipher.TryDecryptBlock(
        key,
        ciphertext,
        plaintext);

    Assert.True(result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecryptBlock_ПослеШифрования_ВозвращаетИсходныйБлок()
  {
    byte[] key = Convert.FromHexString(
        "8899AABBCCDDEEFF0011223344556677"
      + "FEDCBA98765432100123456789ABCDEF");

    byte[] plaintext = Convert.FromHexString(
        "00112233445566778899AABBCCDDEEFF");

    byte[] ciphertext = new byte[GostKuznyechikCipher.BlockSize];
    byte[] roundtrip = new byte[GostKuznyechikCipher.BlockSize];

    Assert.True(GostKuznyechikCipher.TryEncryptBlock(
        key,
        plaintext,
        ciphertext));

    Assert.True(GostKuznyechikCipher.TryDecryptBlock(
        key,
        ciphertext,
        roundtrip));

    Assert.Equal(plaintext, roundtrip);
  }

  [Fact]
  public void TryEncryptBlock_ПриНекорректнойДлинеКлюча_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize - 1];
    byte[] plaintext = new byte[GostKuznyechikCipher.BlockSize];
    byte[] ciphertext = new byte[GostKuznyechikCipher.BlockSize];

    bool result = GostKuznyechikCipher.TryEncryptBlock(
        key,
        plaintext,
        ciphertext);

    Assert.False(result);
  }

  [Fact]
  public void TryEncryptBlock_ПриНекорректнойДлинеБлока_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize];
    byte[] plaintext = new byte[GostKuznyechikCipher.BlockSize - 1];
    byte[] ciphertext = new byte[GostKuznyechikCipher.BlockSize];

    bool result = GostKuznyechikCipher.TryEncryptBlock(
        key,
        plaintext,
        ciphertext);

    Assert.False(result);
  }

  [Fact]
  public void TryDecryptBlock_ПриНекорректнойДлинеБлока_ВозвращаетFalse()
  {
    byte[] key = new byte[GostKuznyechikCipher.KeySize];
    byte[] ciphertext = new byte[GostKuznyechikCipher.BlockSize - 1];
    byte[] plaintext = new byte[GostKuznyechikCipher.BlockSize];

    bool result = GostKuznyechikCipher.TryDecryptBlock(
        key,
        ciphertext,
        plaintext);

    Assert.False(result);
  }
}
