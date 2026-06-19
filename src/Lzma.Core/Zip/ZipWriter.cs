using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Deflate;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Писатель ZIP-архивов (PKZIP APPNOTE), без unsafe.</para>
/// <para>
/// Пишет локальные заголовки + данные, центральный каталог и EOCD. Для непустых файлов
/// выбирается меньший из вариантов Store (0) и Deflate (8) собственным энкодером. Имена
/// кодируются в UTF-8 (флаг bit 11). ZIP64 и шифрование не поддерживаются.
/// </para>
/// </summary>
public static class ZipWriter
{
  private const uint LocalFileSignature = 0x04034b50;
  private const uint CentralFileSignature = 0x02014b50;
  private const uint EocdSignature = 0x06054b50;

  private const ushort MethodStore = 0;
  private const ushort MethodDeflate = 8;

  private const ushort FlagUtf8 = 1 << 11;
  private const ushort VersionNeeded = 20;          // 2.0
  private const ushort VersionMadeBy = 20;          // host 0 (MS-DOS) | 2.0
  private const ushort DosDate1980 = 0x0021;        // 1980-01-01 (валидная дата)

  private const uint DosAttrDirectory = 0x10;
  private const uint DosAttrArchive = 0x20;

  /// <summary>
  /// Строит ZIP-архив из набора элементов.
  /// </summary>
  public static ZipWriteResult Build(IReadOnlyList<ZipWriterEntry> entries, out byte[] archive)
  {
    archive = [];

    if (entries is null)
      return ZipWriteResult.InvalidData;

    foreach (ZipWriterEntry e in entries)
    {
      if (e is null || e.Content is null || string.IsNullOrEmpty(e.Name) || e.Name.Contains('\0'))
        return ZipWriteResult.InvalidData;

      if (e.IsDirectory && e.Content.Length != 0)
        return ZipWriteResult.InvalidData;
    }

    var output = new List<byte>(1024);
    var central = new List<CentralRecord>(entries.Count);

    foreach (ZipWriterEntry e in entries)
    {
      string name = e.Name.Replace('\\', '/');
      bool isDir = e.IsDirectory;
      if (isDir && !name.EndsWith('/'))
        name += '/';

      byte[] nameBytes = Encoding.UTF8.GetBytes(name);

      ushort method;
      uint crc;
      byte[] data;
      uint uncompSize;

      if (isDir)
      {
        method = MethodStore;
        crc = 0;
        data = [];
        uncompSize = 0;
      }
      else
      {
        byte[] content = e.Content;
        uncompSize = (uint)content.Length;
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

      uint localOffset = (uint)output.Count;

      // ---- Local file header ----
      WriteU32(output, LocalFileSignature);
      WriteU16(output, VersionNeeded);
      WriteU16(output, FlagUtf8);
      WriteU16(output, method);
      WriteU16(output, 0);            // mod time
      WriteU16(output, DosDate1980);  // mod date
      WriteU32(output, crc);
      WriteU32(output, (uint)data.Length);
      WriteU32(output, uncompSize);
      WriteU16(output, (ushort)nameBytes.Length);
      WriteU16(output, 0);            // extra length
      output.AddRange(nameBytes);
      output.AddRange(data);

      central.Add(new CentralRecord(nameBytes, method, crc, (uint)data.Length, uncompSize, localOffset, isDir));
    }

    uint centralStart = (uint)output.Count;

    foreach (CentralRecord r in central)
    {
      WriteU32(output, CentralFileSignature);
      WriteU16(output, VersionMadeBy);
      WriteU16(output, VersionNeeded);
      WriteU16(output, FlagUtf8);
      WriteU16(output, r.Method);
      WriteU16(output, 0);            // mod time
      WriteU16(output, DosDate1980);  // mod date
      WriteU32(output, r.Crc);
      WriteU32(output, r.CompSize);
      WriteU32(output, r.UncompSize);
      WriteU16(output, (ushort)r.NameBytes.Length);
      WriteU16(output, 0);            // extra length
      WriteU16(output, 0);            // comment length
      WriteU16(output, 0);            // disk number start
      WriteU16(output, 0);            // internal attributes
      WriteU32(output, r.IsDirectory ? DosAttrDirectory : DosAttrArchive);
      WriteU32(output, r.LocalOffset);
      output.AddRange(r.NameBytes);
    }

    uint centralSize = (uint)output.Count - centralStart;

    // ---- End of central directory ----
    WriteU32(output, EocdSignature);
    WriteU16(output, 0);                       // disk number
    WriteU16(output, 0);                       // disk with central dir
    WriteU16(output, (ushort)central.Count);   // entries on this disk
    WriteU16(output, (ushort)central.Count);   // total entries
    WriteU32(output, centralSize);
    WriteU32(output, centralStart);
    WriteU16(output, 0);                       // comment length

    archive = [.. output];
    return ZipWriteResult.Ok;
  }

  private readonly record struct CentralRecord(
      byte[] NameBytes,
      ushort Method,
      uint Crc,
      uint CompSize,
      uint UncompSize,
      uint LocalOffset,
      bool IsDirectory);

  private static void WriteU16(List<byte> output, ushort v)
  {
    output.Add((byte)v);
    output.Add((byte)(v >> 8));
  }

  private static void WriteU32(List<byte> output, uint v)
  {
    output.Add((byte)v);
    output.Add((byte)(v >> 8));
    output.Add((byte)(v >> 16));
    output.Add((byte)(v >> 24));
  }
}
