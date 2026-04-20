using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipAesDecryptorTests
{
  [Fact]
  public void TryDecryptCbcNoPadding_ДляNistAes256CbcVector_ВозвращаетPlaintext()
  {
    byte[] key = Convert.FromHexString(
        "603DEB1015CA71BE2B73AEF0857D7781"
      + "1F352C073B6108D72D9810A30914DFF4");

    byte[] iv = Convert.FromHexString(
        "000102030405060708090A0B0C0D0E0F");

    byte[] ciphertext = Convert.FromHexString(
        "F58C4C04D6E5F1BA779EABFB5F7BFBD6"
      + "9CFC4E967EDB808D679F777BC6702C7D"
      + "39F23369A9D9BACFA530E26304231461"
      + "B2EB05E2C39BE9FCDA6C19078C6A9D1B");

    byte[] expectedPlaintext = Convert.FromHexString(
        "6BC1BEE22E409F96E93D7E117393172A"
      + "AE2D8A571E03AC9C9EB76FAC45AF8E51"
      + "30C81C46A35CE411E5FBC1191A0A52EF"
      + "F69F2445DF4F9B17AD2B417BE66C3710");

    bool result = SevenZipAesDecryptor.TryDecryptCbcNoPadding(
        key,
        iv,
        ciphertext,
        out byte[] plaintext);

    Assert.True(result);
    Assert.Equal(expectedPlaintext, plaintext);
  }

  [Fact]
  public void TryDecryptCbcNoPadding_ПустойCiphertext_ВозвращаетПустойPlaintext()
  {
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    byte[] iv = new byte[SevenZipAesDecryptor.AesBlockSize];

    bool result = SevenZipAesDecryptor.TryDecryptCbcNoPadding(
        key,
        iv,
        ciphertext: [],
        plaintext: out byte[] plaintext);

    Assert.True(result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecryptCbcNoPadding_НекорректнаяДлинаКлюча_ВозвращаетFalse()
  {
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize - 1];
    byte[] iv = new byte[SevenZipAesDecryptor.AesBlockSize];
    byte[] ciphertext = new byte[SevenZipAesDecryptor.AesBlockSize];

    bool result = SevenZipAesDecryptor.TryDecryptCbcNoPadding(
        key,
        iv,
        ciphertext,
        out byte[] plaintext);

    Assert.False(result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecryptCbcNoPadding_НекорректнаяДлинаIv_ВозвращаетFalse()
  {
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    byte[] iv = new byte[SevenZipAesDecryptor.AesBlockSize - 1];
    byte[] ciphertext = new byte[SevenZipAesDecryptor.AesBlockSize];

    bool result = SevenZipAesDecryptor.TryDecryptCbcNoPadding(
        key,
        iv,
        ciphertext,
        out byte[] plaintext);

    Assert.False(result);
    Assert.Empty(plaintext);
  }

  [Fact]
  public void TryDecryptCbcNoPadding_CiphertextНеКратенБлоку_ВозвращаетFalse()
  {
    byte[] key = new byte[SevenZipAesKeyDerivation.Aes256KeySize];
    byte[] iv = new byte[SevenZipAesDecryptor.AesBlockSize];
    byte[] ciphertext = new byte[SevenZipAesDecryptor.AesBlockSize + 1];

    bool result = SevenZipAesDecryptor.TryDecryptCbcNoPadding(
        key,
        iv,
        ciphertext,
        out byte[] plaintext);

    Assert.False(result);
    Assert.Empty(plaintext);
  }
}
