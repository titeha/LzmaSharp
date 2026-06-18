using System.Buffers.Binary;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Вспомогательные методы для <see cref="SevenZipSignatureHeader"/>.
/// </summary>
public static class SevenZipSignatureHeaderExtensions
{
  /// <summary>
  /// Возвращает байты StartHeader (20 байт), которые участвуют в вычислении CRC заголовка.
  /// Формат: NextHeaderOffset (8 LE) + NextHeaderSize (8 LE) + NextHeaderCrc (4 LE).
  /// </summary>
  public static byte[] GetStartHeaderBytes(this SevenZipSignatureHeader header)
  {
    // StartHeader в формате 7z всегда 20 байт.
    Span<byte> tmp = stackalloc byte[20];

    BinaryPrimitives.WriteUInt64LittleEndian(tmp[..8], header.NextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(tmp.Slice(8, 8), header.NextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(tmp.Slice(16, 4), header.NextHeaderCrc);

    return tmp.ToArray();
  }
}
