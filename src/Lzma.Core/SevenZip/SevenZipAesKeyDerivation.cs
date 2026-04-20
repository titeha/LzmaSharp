using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Derivation ключа для 7zAES.
/// </summary>
public static class SevenZipAesKeyDerivation
{
  /// <summary>
  /// Размер AES-256 ключа в байтах.
  /// </summary>
  public const int Aes256KeySize = 32;

  /// <summary>
  /// Пытается построить AES-256 ключ для специального режима 7zAES
  /// с <see cref="SevenZipAesCoder.DirectKeyNumCyclesPower"/>.
  /// </summary>
  public static bool TryDeriveDirectKey(
      SevenZipAesProperties properties,
      SevenZipPassword password,
      Span<byte> destinationKey)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    if (destinationKey.Length < Aes256KeySize)
      throw new ArgumentException("Буфер назначения меньше размера AES-256 ключа.", nameof(destinationKey));

    destinationKey[..Aes256KeySize].Clear();

    if (properties.NumCyclesPower != SevenZipAesCoder.DirectKeyNumCyclesPower)
      return false;

    if (properties.Salt.Length > Aes256KeySize)
      return false;

    properties.Salt.CopyTo(destinationKey);

    int passwordOffset = properties.Salt.Length;
    int passwordCapacity = Aes256KeySize - passwordOffset;

    if (passwordCapacity <= 0)
      return true;

    byte[] passwordBytes = password.ToUtf16LeByteArray();

    try
    {
      int passwordBytesToCopy = Math.Min(passwordBytes.Length, passwordCapacity);
      passwordBytes.AsSpan(0, passwordBytesToCopy)
          .CopyTo(destinationKey.Slice(passwordOffset, passwordBytesToCopy));

      return true;
    }
    finally
    {
      System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
    }
  }

  /// <summary>
  /// Пытается построить AES-256 ключ для обычного режима 7zAES
  /// через SHA-256 derivation.
  /// </summary>
  public static bool TryDeriveSha256Key(
      SevenZipAesProperties properties,
      SevenZipPassword password,
      Span<byte> destinationKey)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    if (destinationKey.Length < Aes256KeySize)
      throw new ArgumentException("Буфер назначения меньше размера AES-256 ключа.", nameof(destinationKey));

    destinationKey[..Aes256KeySize].Clear();

    if (properties.NumCyclesPower == SevenZipAesCoder.DirectKeyNumCyclesPower)
      return false;

    if (!SevenZipAesCoder.IsSupportedNumCyclesPower(properties.NumCyclesPower))
      return false;

    byte[] passwordBytes = password.ToUtf16LeByteArray();
    byte[]? loopBuffer = null;

    try
    {
      int loopBufferSize = checked(properties.Salt.Length + passwordBytes.Length + 8);
      loopBuffer = new byte[loopBufferSize];

      properties.Salt.CopyTo(loopBuffer.AsSpan(0, properties.Salt.Length));
      passwordBytes.CopyTo(loopBuffer.AsSpan(properties.Salt.Length, passwordBytes.Length));

      using IncrementalHash sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

      ulong rounds = 1UL << properties.NumCyclesPower;
      Span<byte> counterBytes = loopBuffer.AsSpan(loopBuffer.Length - 8, 8);

      for (ulong counter = 0; counter < rounds; counter++)
      {
        BinaryPrimitives.WriteUInt64LittleEndian(counterBytes, counter);
        sha256.AppendData(loopBuffer);
      }

      if (!sha256.TryGetHashAndReset(destinationKey[..Aes256KeySize], out int bytesWritten)
          || bytesWritten != Aes256KeySize)
        throw new InvalidOperationException("Не удалось построить SHA-256 ключ для 7zAES.");

      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(passwordBytes);

      if (loopBuffer is not null)
        CryptographicOperations.ZeroMemory(loopBuffer);
    }
  }
}
