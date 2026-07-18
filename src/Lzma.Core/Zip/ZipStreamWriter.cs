using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Deflate;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Потоковый писатель ZIP в seekable-<see cref="Stream"/> — без удержания всего архива в памяти.</para>
/// <para>
/// Каждый файл читается по одному (≤ 2 ГиБ), сжимается меньшим из Store(0)/Deflate(8) одноразовым
/// энкодером и пишется в выход; центральный каталог и EOCD дописываются в конце. Смещения/счётчик —
/// 64-битные, при переполнении 4 ГиБ / 65535 записей пишется ZIP64 (extra <c>0x0001</c> + ZIP64 EOCD),
/// поэтому итоговый архив может быть больше 4 ГиБ. Имена — UTF-8 (флаг bit 11).
/// </para>
/// <para>Отдельный файл больше 2 ГиБ пока не поддержан (нужен потоковый Deflate/Store) —
/// <see cref="ZipWriteResult.NotSupported"/>.</para>
/// </summary>
public static class ZipStreamWriter
{
  private const uint LocalFileSignature = 0x04034b50;
  private const uint CentralFileSignature = 0x02014b50;
  private const uint EocdSignature = 0x06054b50;
  private const uint Zip64EocdSignature = 0x06064b50;
  private const uint Zip64EocdLocatorSignature = 0x07064b50;

  private const ushort Zip64ExtraId = 0x0001;

  private const ushort MethodStore = 0;
  private const ushort MethodDeflate = 8;

  private const ushort FlagUtf8 = 1 << 11;
  private const ushort VersionBase = 20;   // 2.0
  private const ushort VersionZip64 = 45;  // 4.5
  private const ushort DosDate1980 = 0x0021;

  private const uint DosAttrDirectory = 0x10;
  private const uint DosAttrArchive = 0x20;

  private const uint Zip64Sentinel32 = 0xFFFFFFFF;
  private const ushort Zip64Sentinel16 = 0xFFFF;

  /// <summary>
  /// Пишет ZIP-архив из <paramref name="entries"/> в seekable-поток <paramref name="output"/>.
  /// </summary>
  public static ZipWriteResult Write(
      IReadOnlyList<ZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default,
      IProgress<string>? currentFile = null)
  {
    if (entries is null || output is null || !output.CanWrite || !output.CanSeek)
      return ZipWriteResult.InvalidData;

    foreach (ZipStreamingEntry e in entries)
    {
      if (e is null || string.IsNullOrEmpty(e.Name) || e.Name.Contains('\0'))
        return ZipWriteResult.InvalidData;

      if (!e.IsDirectory && e.Length > int.MaxValue)
        return ZipWriteResult.NotSupported; // отдельный файл > 2 ГиБ пока не поддержан
    }

    long total = 0;
    foreach (ZipStreamingEntry e in entries)
      if (!e.IsDirectory)
        total += e.Length;

    var central = new List<CentralRecord>(entries.Count);
    long processed = 0;

    foreach (ZipStreamingEntry e in entries)
    {
      token.ThrowIfCancellationRequested();

      string name = e.Name.Replace('\\', '/');
      bool isDir = e.IsDirectory;
      if (isDir && !name.EndsWith('/'))
        name += '/';

      byte[] nameBytes = Encoding.UTF8.GetBytes(name);
      long localOffset = output.Position;

      currentFile?.Report(name);

      ushort method;
      uint crc;
      byte[] data;
      long uncompSize;

      if (isDir)
      {
        method = MethodStore;
        crc = 0;
        data = [];
        uncompSize = 0;
      }
      else
      {
        byte[] content;
        try
        {
          content = ReadEntry(e);
        }
        catch (IOException)
        {
          return ZipWriteResult.InvalidData;
        }

        uncompSize = content.Length;
        crc = Crc32.Compute(content);

        byte[] deflated = content.Length == 0 ? [] : DeflateEncoder.Encode(content);
        if (deflated.Length != 0 && deflated.Length < content.Length)
        {
          method = MethodDeflate;
          data = deflated;
        }
        else
        {
          method = MethodStore;
          data = content;
        }
      }

      long compSize = data.Length;

      // Файлы ≤ 2 ГиБ → размеры в 32 бита помещаются; ZIP64 нужен лишь при большом смещении.
      bool zip64Offset = localOffset >= Zip64Sentinel32;

      WriteLocalHeader(output, nameBytes, method, crc, compSize, uncompSize);
      output.Write(data, 0, data.Length);

      central.Add(new CentralRecord(nameBytes, method, crc, compSize, uncompSize, localOffset, isDir, zip64Offset));

      processed += uncompSize;
      progress?.Report(new SevenZipProgress(processed, total));
    }

    long centralStart = output.Position;

    foreach (CentralRecord r in central)
      WriteCentralHeader(output, r);

    long centralSize = output.Position - centralStart;

    WriteEndRecords(output, central.Count, centralStart, centralSize);

    return ZipWriteResult.Ok;
  }

  // Читает ровно Length байт элемента (файл ≤ 2 ГиБ, проверено выше).
  private static byte[] ReadEntry(ZipStreamingEntry e)
  {
    byte[] content = new byte[e.Length];
    using Stream source = e.OpenRead();
    source.ReadExactly(content, 0, content.Length);
    return content;
  }

  private static void WriteLocalHeader(Stream output, byte[] nameBytes, ushort method, uint crc, long compSize, long uncompSize)
  {
    WriteU32(output, LocalFileSignature);
    WriteU16(output, VersionBase);
    WriteU16(output, FlagUtf8);
    WriteU16(output, method);
    WriteU16(output, 0);              // mod time
    WriteU16(output, DosDate1980);    // mod date
    WriteU32(output, crc);
    WriteU32(output, (uint)compSize);
    WriteU32(output, (uint)uncompSize);
    WriteU16(output, (ushort)nameBytes.Length);
    WriteU16(output, 0);              // extra length (размеры ≤ 2 ГиБ → ZIP64 в локальном не нужен)
    output.Write(nameBytes, 0, nameBytes.Length);
  }

  private static void WriteCentralHeader(Stream output, CentralRecord r)
  {
    // ZIP64 extra в центральном заголовке — только для большого смещения (размеры ≤ 2 ГиБ).
    ushort extraLen = r.Zip64Offset ? (ushort)(4 + 8) : (ushort)0;
    ushort versionNeeded = r.Zip64Offset ? VersionZip64 : VersionBase;
    uint offset32 = r.Zip64Offset ? Zip64Sentinel32 : (uint)r.LocalOffset;

    WriteU32(output, CentralFileSignature);
    WriteU16(output, versionNeeded);   // version made by
    WriteU16(output, versionNeeded);   // version needed
    WriteU16(output, FlagUtf8);
    WriteU16(output, r.Method);
    WriteU16(output, 0);               // mod time
    WriteU16(output, DosDate1980);     // mod date
    WriteU32(output, r.Crc);
    WriteU32(output, (uint)r.CompSize);
    WriteU32(output, (uint)r.UncompSize);
    WriteU16(output, (ushort)r.NameBytes.Length);
    WriteU16(output, extraLen);
    WriteU16(output, 0);               // comment length
    WriteU16(output, 0);               // disk number start
    WriteU16(output, 0);               // internal attributes
    WriteU32(output, r.IsDirectory ? DosAttrDirectory : DosAttrArchive);
    WriteU32(output, offset32);
    output.Write(r.NameBytes, 0, r.NameBytes.Length);

    if (r.Zip64Offset)
    {
      WriteU16(output, Zip64ExtraId);
      WriteU16(output, 8);             // размер данных extra (только смещение)
      WriteU64(output, (ulong)r.LocalOffset);
    }
  }

  private static void WriteEndRecords(Stream output, int count, long centralStart, long centralSize)
  {
    bool zip64 = count > Zip64Sentinel16 || centralStart > Zip64Sentinel32 || centralSize > Zip64Sentinel32;

    if (zip64)
    {
      long zip64EocdOffset = output.Position;

      WriteU32(output, Zip64EocdSignature);
      WriteU64(output, 44);                        // размер записи (56 - 12)
      WriteU16(output, VersionZip64);              // version made by
      WriteU16(output, VersionZip64);              // version needed
      WriteU32(output, 0);                         // disk number
      WriteU32(output, 0);                         // disk with CD
      WriteU64(output, (ulong)count);              // entries this disk
      WriteU64(output, (ulong)count);              // total entries
      WriteU64(output, (ulong)centralSize);
      WriteU64(output, (ulong)centralStart);

      WriteU32(output, Zip64EocdLocatorSignature);
      WriteU32(output, 0);                         // disk with ZIP64 EOCD
      WriteU64(output, (ulong)zip64EocdOffset);
      WriteU32(output, 1);                         // total disks
    }

    WriteU32(output, EocdSignature);
    WriteU16(output, 0);                                                        // disk number
    WriteU16(output, 0);                                                        // disk with CD
    WriteU16(output, count > Zip64Sentinel16 ? Zip64Sentinel16 : (ushort)count); // entries this disk
    WriteU16(output, count > Zip64Sentinel16 ? Zip64Sentinel16 : (ushort)count); // total entries
    WriteU32(output, centralSize > Zip64Sentinel32 ? Zip64Sentinel32 : (uint)centralSize);
    WriteU32(output, centralStart > Zip64Sentinel32 ? Zip64Sentinel32 : (uint)centralStart);
    WriteU16(output, 0);                                                        // comment length
  }

  private readonly record struct CentralRecord(
      byte[] NameBytes,
      ushort Method,
      uint Crc,
      long CompSize,
      long UncompSize,
      long LocalOffset,
      bool IsDirectory,
      bool Zip64Offset);

  private static void WriteU16(Stream output, ushort value)
  {
    Span<byte> b = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(b, value);
    output.Write(b);
  }

  private static void WriteU32(Stream output, uint value)
  {
    Span<byte> b = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(b, value);
    output.Write(b);
  }

  private static void WriteU64(Stream output, ulong value)
  {
    Span<byte> b = stackalloc byte[8];
    BinaryPrimitives.WriteUInt64LittleEndian(b, value);
    output.Write(b);
  }
}
