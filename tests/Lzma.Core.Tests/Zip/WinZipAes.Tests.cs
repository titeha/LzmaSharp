using System.Security.Cryptography;
using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Крипто-примитив WinZip-AES: KDF (PBKDF2-HMAC-SHA1), AES-CTR (симметричный), HMAC-SHA1 auth-code.
/// Полная сверка формата — интеропом с 7-Zip на уровне архива (отдельные тесты записи/чтения).
/// </summary>
public sealed class WinZipAesTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(15)]
  [InlineData(16)]
  [InlineData(17)]
  [InlineData(100)]
  [InlineData(4096)]
  public void CtrTransform_Симметричен(int length)
  {
    var rnd = new Random(length + 1);
    byte[] key = new byte[32]; rnd.NextBytes(key);
    byte[] data = new byte[length]; rnd.NextBytes(data);
    byte[] original = (byte[])data.Clone();

    WinZipAes.CtrTransform(key, data);              // шифрование
    if (length > 0)
      Assert.NotEqual(original, data);              // что-то поменялось
    WinZipAes.CtrTransform(key, data);              // расшифровка (CTR симметричен)

    Assert.Equal(original, data);
  }

  [Fact]
  public void CtrTransform_ПервыйБлок_СчётчикСтартуетС1()
  {
    // Независимая проверка keystream первого блока: AES-ECB(ключ, counter=[1,0,..,0]).
    byte[] key = new byte[32];
    for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);

    byte[] counter = new byte[16]; counter[0] = 1;
    using Aes aes = Aes.Create();
    aes.Mode = CipherMode.ECB; aes.Padding = PaddingMode.None; aes.Key = key;
    byte[] expectedKeystream = aes.EncryptEcb(counter, PaddingMode.None);

    byte[] data = new byte[16]; // нули → результат = keystream
    WinZipAes.CtrTransform(key, data);

    Assert.Equal(expectedKeystream, data);
  }

  [Fact]
  public void DeriveKeys_Детерминирован_ИКорректныеРазмеры()
  {
    byte[] pw = Encoding.UTF8.GetBytes("пароль123");
    byte[] salt = new byte[16];
    new Random(7).NextBytes(salt);

    WinZipAes.DeriveKeys(pw, salt, WinZipAes.Strength.Aes256, out byte[] k1, out byte[] m1, out byte[] v1);
    WinZipAes.DeriveKeys(pw, salt, WinZipAes.Strength.Aes256, out byte[] k2, out byte[] m2, out byte[] v2);

    Assert.Equal(32, k1.Length);
    Assert.Equal(32, m1.Length);
    Assert.Equal(2, v1.Length);
    Assert.Equal(k1, k2);
    Assert.Equal(m1, m2);
    Assert.Equal(v1, v2);

    // Ключи выводятся непрерывно из одного PBKDF2 — сверяем с прямым вызовом.
    byte[] direct = Rfc2898DeriveBytes.Pbkdf2(pw, salt, 1000, HashAlgorithmName.SHA1, 66);
    Assert.Equal(direct[..32], k1);
    Assert.Equal(direct[32..64], m1);
    Assert.Equal(direct[64..66], v1);
  }

  [Fact]
  public void DeriveKeys_РазнаяСоль_РазныеКлючи()
  {
    byte[] pw = Encoding.UTF8.GetBytes("pw");
    byte[] s1 = new byte[16]; byte[] s2 = new byte[16]; s2[0] = 1;

    WinZipAes.DeriveKeys(pw, s1, WinZipAes.Strength.Aes256, out byte[] k1, out _, out _);
    WinZipAes.DeriveKeys(pw, s2, WinZipAes.Strength.Aes256, out byte[] k2, out _, out _);

    Assert.NotEqual(k1, k2);
  }

  [Fact]
  public void AuthCode_10Байт_Детерминирован()
  {
    byte[] mac = new byte[32]; new Random(1).NextBytes(mac);
    byte[] ct = Encoding.UTF8.GetBytes("ciphertext bytes");

    byte[] a1 = WinZipAes.ComputeAuthenticationCode(mac, ct);
    byte[] a2 = WinZipAes.ComputeAuthenticationCode(mac, ct);

    Assert.Equal(10, a1.Length);
    Assert.Equal(a1, a2);

    // Сверка с полным HMAC-SHA1 (первые 10 байт).
    byte[] full = HMACSHA1.HashData(mac, ct);
    Assert.Equal(full[..10], a1);
  }

  [Theory]
  [InlineData(WinZipAes.Strength.Aes128, 16, 8)]
  [InlineData(WinZipAes.Strength.Aes192, 24, 12)]
  [InlineData(WinZipAes.Strength.Aes256, 32, 16)]
  public void Размеры_КлючаИСоли(WinZipAes.Strength s, int keySize, int saltSize)
  {
    Assert.Equal(keySize, WinZipAes.KeySize(s));
    Assert.Equal(saltSize, WinZipAes.SaltSize(s));
  }
}
