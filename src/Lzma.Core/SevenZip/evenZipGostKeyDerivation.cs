using System.Security.Cryptography;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Derivation ключа для экспериментальных GOST coder-ов LzmaSharp.
/// </summary>
public static class SevenZipGostKeyDerivation
{
  /// <summary>
  /// Размер ключа Кузнечика и Магмы в байтах.
  /// </summary>
  public const int Gost256KeySize = 32;

  /// <summary>
  /// Пытается построить 256-битный ключ для специального direct-режима.
  /// </summary>
  /// <remarks>
  /// Это не production KDF. Метод нужен как маленькая test-friendly ступень
  /// перед подключением полноценного password-based KDF через Стрибог.
  /// </remarks>
  public static bool TryDeriveDirectKey(
      SevenZipGostProperties properties,
      SevenZipPassword password,
      Span<byte> destinationKey)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    if (destinationKey.Length < Gost256KeySize)
      throw new ArgumentException("Буфер назначения меньше размера GOST-ключа.", nameof(destinationKey));

    destinationKey[..Gost256KeySize].Clear();

    if (properties.NumCyclesPower != SevenZipGostCoder.DirectKeyNumCyclesPower)
      return false;

    if (properties.Salt.Length > Gost256KeySize)
      return false;

    properties.Salt.CopyTo(destinationKey);

    int passwordOffset = properties.Salt.Length;
    int passwordCapacity = Gost256KeySize - passwordOffset;

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
      CryptographicOperations.ZeroMemory(passwordBytes);
    }
  }
}
