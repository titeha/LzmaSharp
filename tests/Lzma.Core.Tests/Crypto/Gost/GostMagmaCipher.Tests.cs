using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostMagmaCipherTests
{
  // Официальный тест-вектор RFC 8891 / ГОСТ Р 34.12-2015, Appendix A.
  private const string KeyHex =
      "ffeeddccbbaa99887766554433221100f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff";
  private const string PlaintextHex = "fedcba9876543210";
  private const string CiphertextHex = "4ee901e5c2d8ca3d";

  [Fact]
  public void TryEncryptBlock_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйCiphertext()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] plaintext = Convert.FromHexString(PlaintextHex);
    byte[] expected = Convert.FromHexString(CiphertextHex);

    byte[] ciphertext = new byte[GostMagmaCipher.BlockSize];

    Assert.True(GostMagmaCipher.TryEncryptBlock(key, plaintext, ciphertext));
    Assert.Equal(expected, ciphertext);
  }

  [Fact]
  public void TryDecryptBlock_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйPlaintext()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] ciphertext = Convert.FromHexString(CiphertextHex);
    byte[] expected = Convert.FromHexString(PlaintextHex);

    byte[] plaintext = new byte[GostMagmaCipher.BlockSize];

    Assert.True(GostMagmaCipher.TryDecryptBlock(key, ciphertext, plaintext));
    Assert.Equal(expected, plaintext);
  }

  [Fact]
  public void TryDecryptBlock_ПослеШифрования_ВозвращаетИсходныйБлок()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] plaintext = Convert.FromHexString("0011223344556677");

    byte[] ciphertext = new byte[GostMagmaCipher.BlockSize];
    byte[] roundtrip = new byte[GostMagmaCipher.BlockSize];

    Assert.True(GostMagmaCipher.TryEncryptBlock(key, plaintext, ciphertext));
    Assert.True(GostMagmaCipher.TryDecryptBlock(key, ciphertext, roundtrip));

    Assert.Equal(plaintext, roundtrip);
  }

  [Fact]
  public void TryEncryptBlock_ПриНекорректнойДлинеКлюча_ВозвращаетFalse()
  {
    byte[] key = new byte[GostMagmaCipher.KeySize - 1];
    byte[] plaintext = new byte[GostMagmaCipher.BlockSize];
    byte[] ciphertext = new byte[GostMagmaCipher.BlockSize];

    Assert.False(GostMagmaCipher.TryEncryptBlock(key, plaintext, ciphertext));
  }

  [Fact]
  public void TryEncryptBlock_ПриНекорректнойДлинеБлока_ВозвращаетFalse()
  {
    byte[] key = new byte[GostMagmaCipher.KeySize];
    byte[] plaintext = new byte[GostMagmaCipher.BlockSize - 1];
    byte[] ciphertext = new byte[GostMagmaCipher.BlockSize];

    Assert.False(GostMagmaCipher.TryEncryptBlock(key, plaintext, ciphertext));
  }

  [Fact]
  public void TryDecryptBlock_ПриНекорректнойДлинеБлока_ВозвращаетFalse()
  {
    byte[] key = new byte[GostMagmaCipher.KeySize];
    byte[] ciphertext = new byte[GostMagmaCipher.BlockSize - 1];
    byte[] plaintext = new byte[GostMagmaCipher.BlockSize];

    Assert.False(GostMagmaCipher.TryDecryptBlock(key, ciphertext, plaintext));
  }
}
