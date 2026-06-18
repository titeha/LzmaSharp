using System.Security.Cryptography;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Вспомогательная расшифровка AES для 7zAES.
/// </summary>
public static class SevenZipAesDecryptor
{
  /// <summary>
  /// Размер блока AES в байтах.
  /// </summary>
  public const int AesBlockSize = 16;

  /// <summary>
  /// Пытается расшифровать данные AES-256-CBC без padding.
  /// </summary>
  public static bool TryDecryptCbcNoPadding(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> ciphertext,
      out byte[] plaintext)
  {
    plaintext = [];

    if (key.Length != SevenZipAesKeyDerivation.Aes256KeySize)
      return false;

    if (initializationVector.Length != AesBlockSize)
      return false;

    if (ciphertext.Length % AesBlockSize != 0)
      return false;

    if (ciphertext.Length == 0)
    {
      plaintext = [];
      return true;
    }

    byte[] keyArray = key.ToArray();
    byte[] ivArray = initializationVector.ToArray();
    byte[] ciphertextArray = ciphertext.ToArray();

    try
    {
      using Aes aes = Aes.Create();

      aes.KeySize = 256;
      aes.BlockSize = 128;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.None;

      using ICryptoTransform decryptor = aes.CreateDecryptor(
          keyArray,
          ivArray);

      plaintext = decryptor.TransformFinalBlock(
          ciphertextArray,
          0,
          ciphertextArray.Length);

      return true;
    }
    catch (CryptographicException)
    {
      plaintext = [];
      return false;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(keyArray);
      CryptographicOperations.ZeroMemory(ivArray);
      CryptographicOperations.ZeroMemory(ciphertextArray);
    }
  }
}
