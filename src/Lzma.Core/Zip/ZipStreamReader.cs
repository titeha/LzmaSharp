using System.Buffers.Binary;
using System.Text;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Потоковый читатель ЦЕНТРАЛЬНОГО КАТАЛОГА ZIP из seekable-<see cref="Stream"/>, без загрузки
/// данных в память.</para>
/// <para>
/// Центральный каталог ZIP расположен в КОНЦЕ файла, поэтому по seekable-потоку его можно прочитать,
/// не читая сами сжатые данные — это позволяет открыть/листать очень большие архивы. Возвращает лишь
/// метаданные (<see cref="ZipStreamEntry"/>); распаковка отдельных членов — отдельный потоковый путь.
/// </para>
/// <para>
/// Поддержаны обычные (не-ZIP64) архивы. ZIP64, шифрование и методы кроме Store(0)/Deflate(8)
/// пока возвращают <see cref="ZipReadResult.NotSupported"/> (ZIP64-чтение — следующий шаг).
/// </para>
/// </summary>
public static class ZipStreamReader
{
  private const uint EocdSignature = 0x06054b50;
  private const uint CentralFileSignature = 0x02014b50;

  private const int MethodStore = 0;
  private const int MethodDeflate = 8;

  private const ushort FlagEncrypted = 1 << 0;
  private const ushort FlagUtf8 = 1 << 11;

  private const uint Zip64Sentinel32 = 0xFFFFFFFF;
  private const ushort Zip64Sentinel16 = 0xFFFF;

  private const int EocdSize = 22;
  private const int MaxCommentSize = 0xFFFF;

  /// <summary>
  /// Читает центральный каталог из <paramref name="archive"/> (seekable) в набор метаданных.
  /// </summary>
  public static ZipReadResult ReadCentralDirectory(Stream archive, out ZipStreamEntry[] entries)
  {
    entries = [];

    if (archive is null || !archive.CanSeek || !archive.CanRead)
      return ZipReadResult.InvalidData;

    long length = archive.Length;
    if (length < EocdSize)
      return ZipReadResult.InvalidData;

    if (!TryReadEocd(archive, length, out ushort totalEntries, out uint cdSize, out uint cdOffset))
      return ZipReadResult.InvalidData;

    // ZIP64 (сентинелы) — следующий шаг.
    if (totalEntries == Zip64Sentinel16 || cdOffset == Zip64Sentinel32 || cdSize == Zip64Sentinel32)
      return ZipReadResult.NotSupported;

    if (cdOffset > length || (long)cdOffset + cdSize > length)
      return ZipReadResult.InvalidData;

    // Центральный каталог — метаданные, читаем его целиком в память (много меньше данных архива).
    byte[] central = new byte[cdSize];
    archive.Position = cdOffset;
    try
    {
      archive.ReadExactly(central, 0, (int)cdSize);
    }
    catch (EndOfStreamException)
    {
      return ZipReadResult.InvalidData;
    }

    var list = new List<ZipStreamEntry>(totalEntries);
    int pos = 0;

    for (int i = 0; i < totalEntries; i++)
    {
      if (pos + 46 > central.Length || BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos, 4)) != CentralFileSignature)
        return ZipReadResult.InvalidData;

      ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 8, 2));
      ushort method = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 10, 2));
      uint crc = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 16, 4));
      uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 20, 4));
      uint uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 24, 4));
      int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 28, 2));
      int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 30, 2));
      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 32, 2));
      uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 42, 4));

      if ((flags & FlagEncrypted) != 0)
        return ZipReadResult.NotSupported;

      // ZIP64-сентинелы в размерах/смещении — следующий шаг.
      if (compSize == Zip64Sentinel32 || uncompSize == Zip64Sentinel32 || localOffset == Zip64Sentinel32)
        return ZipReadResult.NotSupported;

      if (pos + 46 + nameLen > central.Length)
        return ZipReadResult.InvalidData;

      string name = DecodeName(central.AsSpan(pos + 46, nameLen), flags);
      bool isDirectory = name.EndsWith('/');

      if (!isDirectory && method != MethodStore && method != MethodDeflate)
        return ZipReadResult.NotSupported;

      list.Add(new ZipStreamEntry(
          name,
          method,
          crc,
          compSize,
          uncompSize,
          localOffset,
          isDirectory,
          flags));

      pos += 46 + nameLen + extraLen + commentLen;
    }

    entries = [.. list];
    return ZipReadResult.Ok;
  }

  /// <summary>
  /// Находит End Of Central Directory, читая хвост файла (комментарий до 64 КБ), и извлекает поля.
  /// </summary>
  private static bool TryReadEocd(Stream archive, long length, out ushort totalEntries, out uint cdSize, out uint cdOffset)
  {
    totalEntries = 0;
    cdSize = 0;
    cdOffset = 0;

    int tailLen = (int)Math.Min(length, EocdSize + MaxCommentSize);
    long start = length - tailLen;

    byte[] tail = new byte[tailLen];
    archive.Position = start;
    try
    {
      archive.ReadExactly(tail, 0, tailLen);
    }
    catch (EndOfStreamException)
    {
      return false;
    }

    for (int p = tailLen - EocdSize; p >= 0; p--)
    {
      if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p, 4)) != EocdSignature)
        continue;

      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 20, 2));

      // Длина комментария должна совпадать с остатком до конца файла.
      if (start + p + EocdSize + commentLen != length)
        continue;

      totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 10, 2));
      cdSize = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p + 12, 4));
      cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p + 16, 4));
      return true;
    }

    return false;
  }

  private static string DecodeName(ReadOnlySpan<byte> raw, ushort flags)
  {
    Encoding encoding = (flags & FlagUtf8) != 0 ? Encoding.UTF8 : Encoding.Latin1;
    return encoding.GetString(raw).Replace('\\', '/');
  }
}
