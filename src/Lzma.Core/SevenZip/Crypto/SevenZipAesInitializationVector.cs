namespace Lzma.Core.SevenZip;

/// <summary>
/// Подготовка initialization vector для 7zAES.
/// </summary>
public static class SevenZipAesInitializationVector
{
  /// <summary>
  /// Строит полный 16-байтовый IV для AES-CBC из свойств 7zAES coder-а.
  /// </summary>
  public static bool TryBuild(
      SevenZipAesProperties properties,
      Span<byte> destination)
  {
    ArgumentNullException.ThrowIfNull(properties);

    if (destination.Length < SevenZipAesDecryptor.AesBlockSize)
      throw new ArgumentException("Буфер назначения меньше размера AES IV.", nameof(destination));

    Span<byte> iv = destination[..SevenZipAesDecryptor.AesBlockSize];
    iv.Clear();

    byte[] source = properties.InitializationVector;

    if (source.Length > SevenZipAesDecryptor.AesBlockSize)
      return false;

    source.CopyTo(iv);
    return true;
  }

  /// <summary>
  /// Строит полный 16-байтовый IV для AES-CBC из свойств 7zAES coder-а.
  /// </summary>
  public static bool TryBuild(
      SevenZipAesProperties properties,
      out byte[] initializationVector)
  {
    initializationVector = new byte[SevenZipAesDecryptor.AesBlockSize];

    if (!TryBuild(properties, initializationVector))
    {
      initializationVector = [];
      return false;
    }

    return true;
  }
}
