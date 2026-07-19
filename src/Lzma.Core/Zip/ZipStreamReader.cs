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
/// Поддержаны обычные архивы и ZIP64 (архивы &gt;4 ГиБ и/или &gt;65535 записей). Шифрование и методы
/// кроме Store(0)/Deflate(8) возвращают <see cref="ZipReadResult.NotSupported"/>.
/// </para>
/// </summary>
public static class ZipStreamReader
{
  private const uint EocdSignature = 0x06054b50;
  private const uint CentralFileSignature = 0x02014b50;
  private const uint Zip64EocdLocatorSignature = 0x07064b50;
  private const uint Zip64EocdSignature = 0x06064b50;

  private const ushort Zip64ExtraId = 0x0001;

  private const int MethodStore = 0;
  private const int MethodDeflate = 8;

  private const ushort FlagEncrypted = 1 << 0;
  private const ushort FlagUtf8 = 1 << 11;

  private const uint Zip64Sentinel32 = 0xFFFFFFFF;
  private const ushort Zip64Sentinel16 = 0xFFFF;

  private const int EocdSize = 22;
  private const int Zip64LocatorSize = 20;
  private const int Zip64EocdMinSize = 56;
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

    ZipReadResult eocd = TryReadEocd(archive, length, out long totalEntries, out long cdSize, out long cdOffset);
    if (eocd != ZipReadResult.Ok)
      return eocd;

    if (cdOffset < 0 || cdSize < 0 || cdOffset > length || cdOffset + cdSize > length)
      return ZipReadResult.InvalidData;

    // Центральный каталог — метаданные (много меньше данных архива), читаем его целиком в память.
    // Каталог >2 ГиБ (сотни миллионов записей) в один буфер не помещается — пока не поддержан.
    if (cdSize > int.MaxValue)
      return ZipReadResult.NotSupported;

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

    var list = new List<ZipStreamEntry>();
    int pos = 0;

    for (long i = 0; i < totalEntries; i++)
    {
      if (pos + 46 > central.Length || BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos, 4)) != CentralFileSignature)
        return ZipReadResult.InvalidData;

      ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 8, 2));
      ushort method = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 10, 2));
      uint crc = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 16, 4));
      uint compSize32 = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 20, 4));
      uint uncompSize32 = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 24, 4));
      int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 28, 2));
      int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 30, 2));
      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(central.AsSpan(pos + 32, 2));
      uint localOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(central.AsSpan(pos + 42, 4));

      if (pos + 46 + nameLen + extraLen > central.Length)
        return ZipReadResult.InvalidData;

      ReadOnlySpan<byte> extra = central.AsSpan(pos + 46 + nameLen, extraLen);

      long compSize = compSize32;
      long uncompSize = uncompSize32;
      long localOffset = localOffset32;

      bool needUncomp = uncompSize32 == Zip64Sentinel32;
      bool needComp = compSize32 == Zip64Sentinel32;
      bool needOffset = localOffset32 == Zip64Sentinel32;

      if (needUncomp || needComp || needOffset)
      {
        if (!TryApplyZip64Extra(extra, ref compSize, ref uncompSize, ref localOffset, needUncomp, needComp, needOffset))
          return ZipReadResult.InvalidData;
      }

      string name = DecodeName(central.AsSpan(pos + 46, nameLen), flags);
      bool isDirectory = name.EndsWith('/');

      // WinZip-AES: метод 99 + extra 0x9901 → реальный метод и сила. Legacy ZipCrypto — не поддержан.
      ushort effectiveMethod = method;
      bool isEncrypted = false;
      WinZipAes.Strength strength = default;
      if (method == WinZipAes.EncryptionMethod)
      {
        if (!TryFindAesExtra(extra, out strength, out effectiveMethod))
          return ZipReadResult.NotSupported;
        isEncrypted = true;
      }
      else if ((flags & FlagEncrypted) != 0)
      {
        return ZipReadResult.NotSupported;
      }

      if (!isDirectory && effectiveMethod != MethodStore && effectiveMethod != MethodDeflate)
        return ZipReadResult.NotSupported;

      list.Add(new ZipStreamEntry(name, effectiveMethod, crc, compSize, uncompSize, localOffset, isDirectory, flags, isEncrypted, strength));

      pos += 46 + nameLen + extraLen + commentLen;
    }

    entries = [.. list];
    return ZipReadResult.Ok;
  }

  /// <summary>
  /// Находит EOCD (комментарий до 64 КБ) и извлекает 64-битные величины каталога, при необходимости
  /// разбирая ZIP64 EOCD-локатор и ZIP64 EOCD-запись.
  /// </summary>
  private static ZipReadResult TryReadEocd(Stream archive, long length, out long totalEntries, out long cdSize, out long cdOffset)
  {
    totalEntries = 0;
    cdSize = 0;
    cdOffset = 0;

    int tailLen = (int)Math.Min(length, EocdSize + MaxCommentSize);
    long tailStart = length - tailLen;

    byte[] tail = new byte[tailLen];
    archive.Position = tailStart;
    try
    {
      archive.ReadExactly(tail, 0, tailLen);
    }
    catch (EndOfStreamException)
    {
      return ZipReadResult.InvalidData;
    }

    for (int p = tailLen - EocdSize; p >= 0; p--)
    {
      if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p, 4)) != EocdSignature)
        continue;

      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 20, 2));
      if (tailStart + p + EocdSize + commentLen != length)
        continue;

      ushort entries16 = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(p + 10, 2));
      uint cdSize32 = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p + 12, 4));
      uint cdOffset32 = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(p + 16, 4));

      // Обычный ZIP: все величины помещаются в 16/32 бита.
      if (entries16 != Zip64Sentinel16 && cdSize32 != Zip64Sentinel32 && cdOffset32 != Zip64Sentinel32)
      {
        totalEntries = entries16;
        cdSize = cdSize32;
        cdOffset = cdOffset32;
        return ZipReadResult.Ok;
      }

      // ZIP64: истинные величины — в ZIP64 EOCD-записи, найденной через локатор перед EOCD.
      return TryReadZip64Eocd(archive, tailStart + p, out totalEntries, out cdSize, out cdOffset);
    }

    return ZipReadResult.InvalidData;
  }

  // Читает ZIP64 EOCD-локатор (20 б перед EOCD) и по нему — ZIP64 EOCD-запись с 64-битными величинами.
  private static ZipReadResult TryReadZip64Eocd(Stream archive, long eocdOffset, out long totalEntries, out long cdSize, out long cdOffset)
  {
    totalEntries = 0;
    cdSize = 0;
    cdOffset = 0;

    long locatorOffset = eocdOffset - Zip64LocatorSize;
    if (locatorOffset < 0)
      return ZipReadResult.InvalidData;

    Span<byte> locator = stackalloc byte[Zip64LocatorSize];
    archive.Position = locatorOffset;
    try
    {
      archive.ReadExactly(locator);
    }
    catch (EndOfStreamException)
    {
      return ZipReadResult.InvalidData;
    }

    if (BinaryPrimitives.ReadUInt32LittleEndian(locator[..4]) != Zip64EocdLocatorSignature)
      return ZipReadResult.InvalidData;

    long zip64EocdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(locator.Slice(8, 8));
    if (zip64EocdOffset < 0 || zip64EocdOffset + Zip64EocdMinSize > archive.Length)
      return ZipReadResult.InvalidData;

    Span<byte> record = stackalloc byte[Zip64EocdMinSize];
    archive.Position = zip64EocdOffset;
    try
    {
      archive.ReadExactly(record);
    }
    catch (EndOfStreamException)
    {
      return ZipReadResult.InvalidData;
    }

    if (BinaryPrimitives.ReadUInt32LittleEndian(record[..4]) != Zip64EocdSignature)
      return ZipReadResult.InvalidData;

    totalEntries = (long)BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(32, 8));
    cdSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(40, 8));
    cdOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(record.Slice(48, 8));

    if (totalEntries < 0 || cdSize < 0 || cdOffset < 0)
      return ZipReadResult.InvalidData;

    return ZipReadResult.Ok;
  }

  /// <summary>
  /// Разбирает ZIP64 extra-field (<c>0x0001</c>) записи каталога, подставляя 64-битные размеры/смещение
  /// для тех полей, что помечены сентинелом (порядок по APPNOTE: uncompressed, compressed, offset).
  /// </summary>
  private static bool TryApplyZip64Extra(
      ReadOnlySpan<byte> extra,
      ref long compSize,
      ref long uncompSize,
      ref long localOffset,
      bool needUncomp,
      bool needComp,
      bool needOffset)
  {
    int p = 0;
    while (p + 4 <= extra.Length)
    {
      ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p, 2));
      int size = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p + 2, 2));
      int dataStart = p + 4;

      if (dataStart + size > extra.Length)
        return false;

      if (id == Zip64ExtraId)
      {
        int q = dataStart;
        int end = dataStart + size;

        if (needUncomp)
        {
          if (q + 8 > end) return false;
          uncompSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(q, 8));
          q += 8;
        }

        if (needComp)
        {
          if (q + 8 > end) return false;
          compSize = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(q, 8));
          q += 8;
        }

        if (needOffset)
        {
          if (q + 8 > end) return false;
          localOffset = (long)BinaryPrimitives.ReadUInt64LittleEndian(extra.Slice(q, 8));
        }

        return true;
      }

      p = dataStart + size;
    }

    return false; // ожидали ZIP64 extra, но не нашли
  }

  // Ищет в extra-поле подполе WinZip-AES (0x9901) и разбирает силу шифрования + реальный метод сжатия.
  private static bool TryFindAesExtra(ReadOnlySpan<byte> extra, out WinZipAes.Strength strength, out ushort actualMethod)
  {
    strength = default;
    actualMethod = 0;

    int p = 0;
    while (p + 4 <= extra.Length)
    {
      ushort id = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p, 2));
      int size = BinaryPrimitives.ReadUInt16LittleEndian(extra.Slice(p + 2, 2));
      int dataStart = p + 4;

      if (dataStart + size > extra.Length)
        return false;

      if (id == WinZipAes.ExtraFieldId)
        return WinZipAesMember.TryParseExtraFieldData(extra.Slice(dataStart, size), out _, out strength, out actualMethod);

      p = dataStart + size;
    }

    return false;
  }

  private static string DecodeName(ReadOnlySpan<byte> raw, ushort flags)
  {
    Encoding encoding = (flags & FlagUtf8) != 0 ? Encoding.UTF8 : Encoding.Latin1;
    return encoding.GetString(raw).Replace('\\', '/');
  }
}
