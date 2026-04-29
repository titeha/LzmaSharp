using System.Security.Cryptography;

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
}
