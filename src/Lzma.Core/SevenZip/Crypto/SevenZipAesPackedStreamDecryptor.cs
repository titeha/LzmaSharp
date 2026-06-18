using System.Security.Cryptography;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат расшифровки packed stream через 7zAES.
/// </summary>
public enum SevenZipAesDecryptResult
{
  /// <summary>
  /// Расшифровка выполнена успешно.
  /// </summary>
  Ok = 0,

  /// <summary>
  /// Данные или свойства AES некорректны.
  /// </summary>
  InvalidData = 1,

  /// <summary>
  /// Сценарий корректно распознан, но пока не поддерживается.
  /// </summary>
  NotSupported = 2,
}

/// <summary>
/// Расшифровка packed stream для 7zAES.
/// </summary>
public static class SevenZipAesPackedStreamDecryptor
{
  /// <summary>
  /// Пытается расшифровать packed stream через 7zAES.
  /// </summary>
  public static SevenZipAesDecryptResult TryDecrypt(
      SevenZipAesProperties properties,
      SevenZipPassword password,
      ReadOnlySpan<byte> ciphertext,
      out byte[] plaintext)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    plaintext = [];

    Span<byte> key = stackalloc byte[SevenZipAesKeyDerivation.Aes256KeySize];
    Span<byte> iv = stackalloc byte[SevenZipAesDecryptor.AesBlockSize];

    try
    {
      if (!SevenZipAesKeyDerivation.TryDeriveKey(
          properties,
          password,
          key))
      {
        plaintext = [];
        return SevenZipAesDecryptResult.NotSupported;
      }

      if (!SevenZipAesInitializationVector.TryBuild(
          properties,
          iv))
      {
        plaintext = [];
        return SevenZipAesDecryptResult.InvalidData;
      }

      if (!SevenZipAesDecryptor.TryDecryptCbcNoPadding(
          key,
          iv,
          ciphertext,
          out plaintext))
      {
        plaintext = [];
        return SevenZipAesDecryptResult.InvalidData;
      }

      return SevenZipAesDecryptResult.Ok;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
      CryptographicOperations.ZeroMemory(iv);
    }
  }
}
