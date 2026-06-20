using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.Tests.Crypto.Gost;

public sealed class GostMagmaCtrTransformTests
{
  // Официальный тест-вектор CTR для Магмы, ГОСТ Р 34.12/34.13-2015.
  private const string KeyHex =
      "ffeeddccbbaa99887766554433221100f0f1f2f3f4f5f6f7f8f9fafbfcfdfeff";
  private const string IvHex = "12345678";
  private const string PlaintextHex =
      "92def06b3c130a59db54c704f8189d204a98fb2e67a8024c8912409b17b57e41";
  private const string CiphertextHex =
      "4e98110c97b7b93c3e250d93d6e85d69136d868807b2dbef568eb680ab52a12d";

  [Fact]
  public void TryTransform_ПоОфициальномуТестВектору_ВозвращаетОжидаемыйCiphertext()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] iv = Convert.FromHexString(IvHex);
    byte[] plaintext = Convert.FromHexString(PlaintextHex);
    byte[] expected = Convert.FromHexString(CiphertextHex);

    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, plaintext, out byte[] ciphertext));
    Assert.Equal(expected, ciphertext);
  }

  [Fact]
  public void TryTransform_ОфициальныйCiphertextОбратноДаётPlaintext()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] iv = Convert.FromHexString(IvHex);
    byte[] ciphertext = Convert.FromHexString(CiphertextHex);
    byte[] expected = Convert.FromHexString(PlaintextHex);

    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, ciphertext, out byte[] plaintext));
    Assert.Equal(expected, plaintext);
  }

  [Fact]
  public void TryTransform_RoundTrip_ПроизвольнаяДлина()
  {
    byte[] key = Convert.FromHexString(KeyHex);
    byte[] iv = Convert.FromHexString(IvHex);

    var rnd = new Random(20260620);
    byte[] data = new byte[100]; // не кратно блоку (8) — проверяем хвост
    rnd.NextBytes(data);

    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, data, out byte[] encrypted));
    Assert.True(GostMagmaCtrTransform.TryTransform(key, iv, encrypted, out byte[] decrypted));
    Assert.Equal(data, decrypted);
  }

  [Fact]
  public void TryTransform_НекорректнаяДлинаКлюча_ВозвращаетFalse()
  {
    byte[] key = new byte[GostMagmaCipher.KeySize - 1];
    byte[] iv = Convert.FromHexString(IvHex);

    Assert.False(GostMagmaCtrTransform.TryTransform(key, iv, [1, 2, 3], out _));
  }
}
