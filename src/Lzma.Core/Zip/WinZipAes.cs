using System.Security.Cryptography;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Криптографический примитив WinZip-AES (AE-1/AE-2) — шифрование ZIP-членов, совместимое с
/// 7-Zip/WinZip.</para>
/// <para>
/// Отличается от 7zAES: ключи выводятся PBKDF2-HMAC-SHA1 (1000 итераций), данные шифруются AES в режиме
/// CTR (16-байтовый счётчик, little-endian, старт с 1), целостность — HMAC-SHA1 (первые 10 байт).
/// Раскладка зашифрованного члена: <c>[salt][pwVerify(2)][ciphertext][authCode(10)]</c>.
/// </para>
/// </summary>
public static class WinZipAes
{
  /// <summary>Идентификатор дополнительного поля WinZip-AES в заголовках (0x9901).</summary>
  public const ushort ExtraFieldId = 0x9901;

  /// <summary>Метод сжатия-заглушка в заголовке зашифрованного члена (реальный метод — в extra).</summary>
  public const ushort EncryptionMethod = 99;

  /// <summary>Число итераций PBKDF2 (фиксировано спецификацией WinZip-AES).</summary>
  public const int Iterations = 1000;

  /// <summary>Размер значения проверки пароля (байт).</summary>
  public const int PasswordVerifierSize = 2;

  /// <summary>Размер кода аутентификации HMAC-SHA1 (байт), усечённого от 20.</summary>
  public const int AuthenticationCodeSize = 10;

  private const int AesBlockSize = 16;

  /// <summary>Сила шифрования (значение байта strength в extra-поле 0x9901).</summary>
  public enum Strength : byte
  {
    /// <summary>AES-128.</summary>
    Aes128 = 1,

    /// <summary>AES-192.</summary>
    Aes192 = 2,

    /// <summary>AES-256 (используется при записи).</summary>
    Aes256 = 3,
  }

  /// <summary>Размер соли (байт) для заданной силы: 8/12/16.</summary>
  public static int SaltSize(Strength strength) => KeySize(strength) / 2;

  /// <summary>Размер AES-ключа (байт): 16/24/32.</summary>
  public static int KeySize(Strength strength) => strength switch
  {
    Strength.Aes128 => 16,
    Strength.Aes192 => 24,
    Strength.Aes256 => 32,
    _ => 0,
  };

  /// <summary>Распознан ли байт силы (1..3).</summary>
  public static bool IsValidStrength(byte value) => value is >= 1 and <= 3;

  /// <summary>
  /// Выводит из пароля и соли AES-ключ, MAC-ключ и значение проверки пароля (PBKDF2-HMAC-SHA1).
  /// </summary>
  /// <param name="password">Пароль (обычно UTF-8 байты, без нуль-терминатора).</param>
  public static void DeriveKeys(
      ReadOnlySpan<byte> password,
      ReadOnlySpan<byte> salt,
      Strength strength,
      out byte[] aesKey,
      out byte[] macKey,
      out byte[] passwordVerifier)
  {
    int keyLength = KeySize(strength);

    // PBKDF2 выдаёт непрерывно: AES-ключ | MAC-ключ | 2 байта проверки пароля.
    byte[] derived = Rfc2898DeriveBytes.Pbkdf2(
        password, salt, Iterations, HashAlgorithmName.SHA1, keyLength * 2 + PasswordVerifierSize);

    aesKey = derived[..keyLength];
    macKey = derived[keyLength..(keyLength * 2)];
    passwordVerifier = derived[(keyLength * 2)..];

    CryptographicOperations.ZeroMemory(derived);
  }

  /// <summary>
  /// Преобразует данные AES в режиме CTR НА МЕСТЕ (шифрование и расшифровка идентичны). Счётчик —
  /// 16-байтовый little-endian, стартует с 1 и растёт на каждый 16-байтовый блок.
  /// </summary>
  public static void CtrTransform(ReadOnlySpan<byte> aesKey, Span<byte> data)
  {
    if (data.Length == 0)
      return;

    using Aes aes = Aes.Create();
    aes.Mode = CipherMode.ECB;
    aes.Padding = PaddingMode.None;
    aes.Key = aesKey.ToArray();

    using ICryptoTransform encryptor = aes.CreateEncryptor();

    Span<byte> counter = stackalloc byte[AesBlockSize];
    counter.Clear();
    counter[0] = 1; // старт счётчика (little-endian)

    byte[] counterBlock = new byte[AesBlockSize];
    byte[] keystream = new byte[AesBlockSize];

    for (int offset = 0; offset < data.Length; offset += AesBlockSize)
    {
      counter.CopyTo(counterBlock);
      encryptor.TransformBlock(counterBlock, 0, AesBlockSize, keystream, 0);

      int block = Math.Min(AesBlockSize, data.Length - offset);
      for (int i = 0; i < block; i++)
        data[offset + i] ^= keystream[i];

      IncrementCounter(counter);
    }

    CryptographicOperations.ZeroMemory(keystream);
  }

  /// <summary>Вычисляет код аутентификации (первые 10 байт HMAC-SHA1) над шифртекстом.</summary>
  public static byte[] ComputeAuthenticationCode(ReadOnlySpan<byte> macKey, ReadOnlySpan<byte> ciphertext)
  {
    Span<byte> full = stackalloc byte[HMACSHA1.HashSizeInBytes];
    HMACSHA1.HashData(macKey, ciphertext, full);
    return full[..AuthenticationCodeSize].ToArray();
  }

  // Инкремент 16-байтового счётчика как little-endian 128-битного числа.
  private static void IncrementCounter(Span<byte> counter)
  {
    for (int i = 0; i < counter.Length; i++)
      if (++counter[i] != 0)
        break;
  }
}
