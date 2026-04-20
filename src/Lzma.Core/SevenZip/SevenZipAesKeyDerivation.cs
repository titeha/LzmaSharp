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
}
