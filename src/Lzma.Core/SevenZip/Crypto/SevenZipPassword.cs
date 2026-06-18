using System.Security.Cryptography;
using System.Text;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Парольный материал для 7z-crypto сценариев.
/// </summary>
public sealed class SevenZipPassword : IDisposable
{
  private byte[]? _utf16LeBytes;

  private SevenZipPassword(byte[] utf16LeBytes)
  {
    _utf16LeBytes = utf16LeBytes;
  }

  /// <summary>
  /// Размер парольного материала в байтах UTF-16LE.
  /// </summary>
  public int Utf16LeByteCount => GetUtf16LeBytesOrThrow().Length;

  /// <summary>
  /// Создаёт парольный материал из строки.
  /// </summary>
  public static SevenZipPassword FromString(string password)
  {
    ArgumentNullException.ThrowIfNull(password);

    return FromChars(password.AsSpan());
  }

  /// <summary>
  /// Создаёт парольный материал из символов.
  /// </summary>
  public static SevenZipPassword FromChars(ReadOnlySpan<char> password)
  {
    byte[] bytes = new byte[Encoding.Unicode.GetByteCount(password)];

    int written = Encoding.Unicode.GetBytes(
        password,
        bytes);

    if (written != bytes.Length)
      throw new InvalidOperationException("Не удалось закодировать пароль в UTF-16LE.");

    return new SevenZipPassword(bytes);
  }

  /// <summary>
  /// Копирует парольный материал UTF-16LE в буфер назначения.
  /// </summary>
  public void CopyUtf16LeBytesTo(Span<byte> destination)
  {
    byte[] bytes = GetUtf16LeBytesOrThrow();

    if (destination.Length < bytes.Length)
      throw new ArgumentException("Буфер назначения меньше размера парольного материала.", nameof(destination));

    bytes.CopyTo(destination);
  }

  /// <summary>
  /// Возвращает копию парольного материала UTF-16LE.
  /// </summary>
  public byte[] ToUtf16LeByteArray() => GetUtf16LeBytesOrThrow().ToArray();

  /// <summary>
  /// Очищает внутренний буфер парольного материала.
  /// </summary>
  public void Dispose()
  {
    byte[]? bytes = _utf16LeBytes;
    if (bytes is null)
      return;

    CryptographicOperations.ZeroMemory(bytes);
    _utf16LeBytes = null;
  }

  private byte[] GetUtf16LeBytesOrThrow() => _utf16LeBytes
        ?? throw new ObjectDisposedException(nameof(SevenZipPassword));
}
