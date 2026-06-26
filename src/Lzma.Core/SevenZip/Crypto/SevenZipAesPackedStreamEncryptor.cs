using System.Security.Cryptography;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Шифрование packed stream для 7zAES — инверсия <see cref="SevenZipAesPackedStreamDecryptor"/>.
/// </summary>
/// <remarks>
/// Ключ строится тем же KDF (<see cref="SevenZipAesKeyDerivation"/>), IV дополняется до 16 байт
/// (<see cref="SevenZipAesInitializationVector"/>). Открытый текст добивается нулями до кратности
/// размеру блока AES (декодер обрезает до фактического unpack size). Режим — AES-256-CBC без padding.
/// </remarks>
public static class SevenZipAesPackedStreamEncryptor
{
  /// <summary>
  /// Пытается зашифровать <paramref name="plaintext"/> через 7zAES по заданным свойствам и паролю.
  /// </summary>
  public static SevenZipAesDecryptResult TryEncrypt(
      SevenZipAesProperties properties,
      SevenZipPassword password,
      ReadOnlySpan<byte> plaintext,
      out byte[] ciphertext)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    ciphertext = [];

    Span<byte> key = stackalloc byte[SevenZipAesKeyDerivation.Aes256KeySize];
    Span<byte> iv = stackalloc byte[SevenZipAesDecryptor.AesBlockSize];

    try
    {
      if (!SevenZipAesKeyDerivation.TryDeriveKey(properties, password, key))
        return SevenZipAesDecryptResult.NotSupported;

      if (!SevenZipAesInitializationVector.TryBuild(properties, iv))
        return SevenZipAesDecryptResult.InvalidData;

      return TryEncryptWithKey(key, iv, plaintext, out ciphertext);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
      CryptographicOperations.ZeroMemory(iv);
    }
  }

  /// <summary>
  /// Пытается зашифровать <paramref name="plaintext"/> уже выведенным AES-256 ключом и
  /// полным 16-байтовым IV. Полезно, когда ключ один на архив (KDF считается один раз).
  /// </summary>
  public static SevenZipAesDecryptResult TryEncryptWithKey(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> plaintext,
      out byte[] ciphertext)
  {
    ciphertext = [];

    if (key.Length != SevenZipAesKeyDerivation.Aes256KeySize)
      return SevenZipAesDecryptResult.InvalidData;

    if (initializationVector.Length != SevenZipAesDecryptor.AesBlockSize)
      return SevenZipAesDecryptResult.InvalidData;

    int blockSize = SevenZipAesDecryptor.AesBlockSize;
    int paddedLength = (plaintext.Length + blockSize - 1) / blockSize * blockSize;

    byte[] buffer = new byte[paddedLength];
    plaintext.CopyTo(buffer);

    byte[] keyArray = key.ToArray();
    byte[] ivArray = initializationVector.ToArray();

    try
    {
      using Aes aes = Aes.Create();
      aes.KeySize = 256;
      aes.BlockSize = 128;
      aes.Mode = CipherMode.CBC;
      aes.Padding = PaddingMode.None;

      using ICryptoTransform encryptor = aes.CreateEncryptor(keyArray, ivArray);
      ciphertext = encryptor.TransformFinalBlock(buffer, 0, buffer.Length);

      return SevenZipAesDecryptResult.Ok;
    }
    catch (CryptographicException)
    {
      ciphertext = [];
      return SevenZipAesDecryptResult.InvalidData;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(keyArray);
      CryptographicOperations.ZeroMemory(ivArray);
      CryptographicOperations.ZeroMemory(buffer);
    }
  }
}
