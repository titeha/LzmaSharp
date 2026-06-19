using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Deflate;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Читатель ZIP-архивов (PKZIP APPNOTE), без unsafe.</para>
/// <para>
/// Разбирает End Of Central Directory, центральный каталог и локальные заголовки, затем
/// распаковывает данные методами Store (0) и Deflate (8) собственными реализациями и
/// проверяет CRC-32. ZIP64, шифрование и прочие методы пока возвращают <c>NotSupported</c>.
/// </para>
/// </summary>
public static class ZipReader
{
  private const uint EocdSignature = 0x06054b50;
  private const uint CentralFileSignature = 0x02014b50;
  private const uint LocalFileSignature = 0x04034b50;

  private const int MethodStore = 0;
  private const int MethodDeflate = 8;

  private const ushort FlagEncrypted = 1 << 0;
  private const ushort FlagUtf8 = 1 << 11;

  private const uint Zip64Sentinel32 = 0xFFFFFFFF;
  private const ushort Zip64Sentinel16 = 0xFFFF;

  /// <summary>
  /// Читает ZIP-архив в набор распакованных элементов.
  /// </summary>
  public static ZipReadResult Read(ReadOnlySpan<byte> archive, out ZipEntry[] entries)
  {
    entries = [];

    if (!TryFindEocd(archive, out int eocd))
      return ZipReadResult.InvalidData;

    ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(eocd + 10, 2));
    uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(eocd + 16, 4));

    // ZIP64 пока не поддержан.
    if (totalEntries == Zip64Sentinel16 || cdOffset == Zip64Sentinel32)
      return ZipReadResult.NotSupported;

    if (cdOffset > (uint)archive.Length)
      return ZipReadResult.InvalidData;

    var list = new List<ZipEntry>(totalEntries);
    int pos = (int)cdOffset;

    for (int i = 0; i < totalEntries; i++)
    {
      if (pos + 46 > archive.Length || BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(pos, 4)) != CentralFileSignature)
        return ZipReadResult.InvalidData;

      ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(pos + 8, 2));
      ushort method = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(pos + 10, 2));
      uint crc = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(pos + 16, 4));
      uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(pos + 20, 4));
      uint uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(pos + 24, 4));
      int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(pos + 28, 2));
      int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(pos + 30, 2));
      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(pos + 32, 2));
      uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(pos + 42, 4));

      if ((flags & FlagEncrypted) != 0)
        return ZipReadResult.NotSupported;

      if (compSize == Zip64Sentinel32 || uncompSize == Zip64Sentinel32 || localOffset == Zip64Sentinel32)
        return ZipReadResult.NotSupported;

      if (pos + 46 + nameLen > archive.Length)
        return ZipReadResult.InvalidData;

      string name = DecodeName(archive.Slice(pos + 46, nameLen), flags);
      pos += 46 + nameLen + extraLen + commentLen;

      bool isDirectory = name.EndsWith('/');

      if (isDirectory)
      {
        list.Add(new ZipEntry(name, [], IsDirectory: true));
        continue;
      }

      if (!TryReadLocalData(archive, localOffset, compSize, out ReadOnlySpan<byte> compressed))
        return ZipReadResult.InvalidData;

      byte[] content;
      if (method == MethodStore)
      {
        if (compSize != uncompSize)
          return ZipReadResult.InvalidData;

        content = compressed.ToArray();
      }
      else if (method == MethodDeflate)
      {
        content = new byte[uncompSize];
        DeflateDecodeResult dr = DeflateDecoder.Decode(compressed, content, out _, out int written);
        if (dr != DeflateDecodeResult.Ok || written != uncompSize)
          return ZipReadResult.InvalidData;
      }
      else
      {
        return ZipReadResult.NotSupported;
      }

      if (Crc32.Compute(content) != crc)
        return ZipReadResult.InvalidData;

      list.Add(new ZipEntry(name, content, IsDirectory: false));
    }

    entries = [.. list];
    return ZipReadResult.Ok;
  }

  /// <summary>
  /// По смещению локального заголовка находит начало данных и вырезает compressed-поток.
  /// </summary>
  private static bool TryReadLocalData(ReadOnlySpan<byte> archive, uint localOffset, uint compSize, out ReadOnlySpan<byte> compressed)
  {
    compressed = default;

    int lh = (int)localOffset;
    if (lh < 0 || lh + 30 > archive.Length || BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(lh, 4)) != LocalFileSignature)
      return false;

    int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(lh + 26, 2));
    int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(lh + 28, 2));
    long dataStart = (long)lh + 30 + nameLen + extraLen;

    if (dataStart + compSize > archive.Length)
      return false;

    compressed = archive.Slice((int)dataStart, (int)compSize);
    return true;
  }

  /// <summary>
  /// Ищет End Of Central Directory, сканируя с конца (с учётом комментария до 64 КБ).
  /// </summary>
  private static bool TryFindEocd(ReadOnlySpan<byte> archive, out int position)
  {
    position = -1;

    if (archive.Length < 22)
      return false;

    int minPos = Math.Max(0, archive.Length - 22 - 0xFFFF);

    for (int p = archive.Length - 22; p >= minPos; p--)
    {
      if (BinaryPrimitives.ReadUInt32LittleEndian(archive.Slice(p, 4)) != EocdSignature)
        continue;

      // Длина комментария должна совпадать с остатком до конца файла.
      int commentLen = BinaryPrimitives.ReadUInt16LittleEndian(archive.Slice(p + 20, 2));
      if (p + 22 + commentLen == archive.Length)
      {
        position = p;
        return true;
      }
    }

    return false;
  }

  private static string DecodeName(ReadOnlySpan<byte> raw, ushort flags)
  {
    // Bit 11 => UTF-8; иначе исторически CP437. Для имён файлов Latin1 даёт корректный,
    // обратимый результат для ASCII (а большинство имён ASCII).
    Encoding encoding = (flags & FlagUtf8) != 0 ? Encoding.UTF8 : Encoding.Latin1;
    return encoding.GetString(raw).Replace('\\', '/');
  }
}
