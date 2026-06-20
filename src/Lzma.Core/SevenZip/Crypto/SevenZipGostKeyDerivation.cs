using System.Buffers.Binary;
using System.Security.Cryptography;

using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Формирование ключа для экспериментальных ГОСТ-кодеров LzmaSharp.
/// </summary>
/// <remarks>
/// Сейчас реализован только тестовый direct-key режим.
/// Полноценная парольная функция формирования ключа через Стрибог будет добавлена отдельно.
/// </remarks>
public static class SevenZipGostKeyDerivation
{
  /// <summary>
  /// Размер 256-битного ключа Кузнечика и Магмы в байтах.
  /// </summary>
  public const int Gost256KeySize = 32;

  /// <summary>
  /// Пытается построить 256-битный ключ для специального direct-key режима.
  /// </summary>
  /// <remarks>
  /// Это не промышленная функция формирования ключа. Метод нужен как небольшая
  /// тестовая ступень перед подключением полноценной парольной функции
  /// формирования ключа через Стрибог.
  /// </remarks>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="password">Парольный материал архива.</param>
  /// <param name="destinationKey">Буфер, куда будет записан сформированный ключ.</param>
  /// <returns>
  /// <see langword="true"/>, если ключ удалось построить;
  /// иначе <see langword="false"/>.
  /// </returns>
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

  /// <summary>
  /// Пытается построить 256-битный ключ парольной функцией формирования ключа
  /// через Стрибог-256 (итеративная конструкция в стиле 7z AES, но на Стрибоге).
  /// </summary>
  /// <remarks>
  /// Это собственная (экспериментальная) для GOST-ветки LzmaSharp конструкция:
  /// официального тест-вектора у неё нет, корректность проверяется round-trip-ом
  /// и сверкой малых раундов с прямым вызовом <see cref="GostStribog.Hash256"/>.
  /// Ключ = Стрибог-256( блок_0 || блок_1 || … || блок_{2^p-1} ),
  /// где блок_i = соль || пароль(UTF-16LE) || счётчик_i (8 байт, little-endian).
  /// </remarks>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="password">Парольный материал архива.</param>
  /// <param name="destinationKey">Буфер, куда будет записан сформированный ключ.</param>
  /// <returns>
  /// <see langword="true"/>, если ключ удалось построить;
  /// иначе <see langword="false"/>.
  /// </returns>
  public static bool TryDeriveStribogKey(
      SevenZipGostProperties properties,
      SevenZipPassword password,
      Span<byte> destinationKey)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    if (destinationKey.Length < Gost256KeySize)
      throw new ArgumentException("Буфер назначения меньше размера GOST-ключа.", nameof(destinationKey));

    destinationKey[..Gost256KeySize].Clear();

    if (properties.NumCyclesPower == SevenZipGostCoder.DirectKeyNumCyclesPower)
      return false;

    if (properties.NumCyclesPower > SevenZipGostCoder.SupportedNumCyclesPowerMax)
      return false;

    byte[] passwordBytes = password.ToUtf16LeByteArray();
    byte[]? message = null;

    try
    {
      int blockSize = checked(properties.Salt.Length + passwordBytes.Length + 8);
      ulong rounds = 1UL << properties.NumCyclesPower;
      int messageSize = checked((int)((ulong)blockSize * rounds));
      message = new byte[messageSize];

      // Заполняем все раунды: соль || пароль || счётчик(LE) в каждом блоке.
      for (ulong counter = 0; counter < rounds; counter++)
      {
        Span<byte> block = message.AsSpan(checked((int)((ulong)blockSize * counter)), blockSize);
        properties.Salt.CopyTo(block);
        passwordBytes.CopyTo(block[properties.Salt.Length..]);
        BinaryPrimitives.WriteUInt64LittleEndian(block[(blockSize - 8)..], counter);
      }

      byte[] hash = GostStribog.Hash256(message);

      try
      {
        hash.AsSpan(0, Gost256KeySize).CopyTo(destinationKey);
        return true;
      }
      finally
      {
        CryptographicOperations.ZeroMemory(hash);
      }
    }
    finally
    {
      CryptographicOperations.ZeroMemory(passwordBytes);

      if (message is not null)
        CryptographicOperations.ZeroMemory(message);
    }
  }
}
