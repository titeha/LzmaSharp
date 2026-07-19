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
  private const ushort FlagEncrypted = 1 << 0;
  private const ushort VersionBase = 20;   // 2.0
  private const ushort VersionZip64 = 45;  // 4.5
  private const ushort VersionAes = 51;    // 5.1 (WinZip-AES)
  private const ushort AesExtraTotalSize = 4 + 7; // id/size + данные extra 0x9901
  private const ushort DosDate1980 = 0x0021;

  private const uint DosAttrDirectory = 0x10;
  private const uint DosAttrArchive = 0x20;

  private const uint Zip64Sentinel32 = 0xFFFFFFFF;
  private const ushort Zip64Sentinel16 = 0xFFFF;

  /// <summary>
  /// Пишет ZIP-архив из <paramref name="entries"/> в seekable-поток <paramref name="output"/>. Файлы
  /// сжимаются ПАРАЛЛЕЛЬНО волнами (каждый Deflate на своё ядро), но пишутся строго по порядку —
  /// выход байт-идентичен последовательному.
  /// </summary>
  public static ZipWriteResult Write(
      IReadOnlyList<ZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default,
      IProgress<string>? currentFile = null,
      int maxDegreeOfParallelism = 0,
      byte[]? password = null)
  {
    if (entries is null || output is null || !output.CanWrite || !output.CanSeek)
      return ZipWriteResult.InvalidData;

    bool encrypt = password is not null; // WinZip-AES (AES-256) для всех непустых членов

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

    int dop = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount;
    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = token };
    const long WaveMemoryLimit = 128L * 1024 * 1024; // пик памяти на волну ≈ этот лимит

    var central = new List<CentralRecord>(entries.Count);
    long processed = 0;

    progress?.Report(new SevenZipProgress(0, total));

    int i = 0;
    while (i < entries.Count)
    {
      token.ThrowIfCancellationRequested();

      // Директория — без данных, пишем сразу (сохраняя порядок). Директории не шифруются.
      if (entries[i].IsDirectory)
      {
        WriteEntryRecord(output, central, entries[i].Name, isDir: true, MethodStore, crc: 0, data: [], uncompSize: 0, encrypt: false);
        i++;
        continue;
      }

      // Волна подряд идущих файлов: лимит по числу потоков и по памяти.
      int waveStart = i;
      long waveBytes = 0;
      while (i < entries.Count && !entries[i].IsDirectory)
      {
        int waveCount = i - waveStart;
        if (waveCount >= dop || (waveCount > 0 && waveBytes + entries[i].Length > WaveMemoryLimit))
          break;

        waveBytes += entries[i].Length;
        i++;
      }

      int n = i - waveStart;
      var results = new FileResult[n];

      try
      {
        Parallel.For(0, n, parallelOptions, k =>
        {
          ZipStreamingEntry e = entries[waveStart + k];
          byte[] content = ReadEntry(e);
          uint crc = Crc32.Compute(content);

          byte[] deflated = content.Length == 0 ? [] : DeflateEncoder.Encode(content);
          (ushort method, byte[] data) = deflated.Length != 0 && deflated.Length < content.Length
              ? (MethodDeflate, deflated)
              : (MethodStore, content);

          // Шифруем СЖАТЫЕ данные (Store/Deflate → CTR); реальный метод уйдёт в extra 0x9901.
          if (encrypt)
            data = WinZipAesMember.Encrypt(data, password!, WinZipAes.Strength.Aes256);

          results[k] = new FileResult(method, crc, data);
        });
      }
      catch (AggregateException ex)
      {
        foreach (Exception inner in ex.Flatten().InnerExceptions)
          if (inner is OperationCanceledException)
            throw new OperationCanceledException(token);

        return ZipWriteResult.InvalidData; // ошибка чтения файла в одной из задач
      }

      // Пишем сжатые буферы СТРОГО по порядку волны.
      for (int k = 0; k < n; k++)
      {
        ZipStreamingEntry e = entries[waveStart + k];
        currentFile?.Report(e.Name.Replace('\\', '/'));

        WriteEntryRecord(output, central, e.Name, isDir: false, results[k].Method, results[k].Crc, results[k].Data, e.Length, encrypt);

        processed += e.Length;
        progress?.Report(new SevenZipProgress(processed, total));
      }
    }

    long centralStart = output.Position;

    foreach (CentralRecord r in central)
      WriteCentralHeader(output, r);

    long centralSize = output.Position - centralStart;

    WriteEndRecords(output, central.Count, centralStart, centralSize);

    return ZipWriteResult.Ok;
  }

  // Пишет local header + данные одного элемента и добавляет запись в центральный каталог.
  // uncompSize — исходный размер (для Deflate не равен длине сжатых данных). method — РЕАЛЬНЫЙ метод
  // (Store/Deflate); при encrypt заголовок несёт метод 99, а реальный уходит в extra 0x9901.
  private static void WriteEntryRecord(Stream output, List<CentralRecord> central, string rawName, bool isDir, ushort method, uint crc, byte[] data, long uncompSize, bool encrypt)
  {
    string name = rawName.Replace('\\', '/');
    if (isDir && !name.EndsWith('/'))
      name += '/';

    byte[] nameBytes = Encoding.UTF8.GetBytes(name);
    long localOffset = output.Position;
    long compSize = data.Length;
    bool zip64Offset = localOffset >= Zip64Sentinel32;

    WriteLocalHeader(output, nameBytes, method, crc, compSize, uncompSize, encrypt);
    output.Write(data, 0, data.Length);

    central.Add(new CentralRecord(nameBytes, method, crc, compSize, uncompSize, localOffset, isDir, zip64Offset, encrypt));
  }

  private readonly record struct FileResult(ushort Method, uint Crc, byte[] Data);

  // Читает ровно Length байт элемента (файл ≤ 2 ГиБ, проверено выше).
  private static byte[] ReadEntry(ZipStreamingEntry e)
  {
    byte[] content = new byte[e.Length];
    using Stream source = e.OpenRead();
    source.ReadExactly(content, 0, content.Length);
    return content;
  }

  private static void WriteLocalHeader(Stream output, byte[] nameBytes, ushort method, uint crc, long compSize, long uncompSize, bool encrypt)
  {
    ushort flags = encrypt ? (ushort)(FlagUtf8 | FlagEncrypted) : FlagUtf8;
    ushort headerMethod = encrypt ? WinZipAes.EncryptionMethod : method;
    ushort version = encrypt ? VersionAes : VersionBase;
    ushort extraLen = encrypt ? AesExtraTotalSize : (ushort)0;

    WriteU32(output, LocalFileSignature);
    WriteU16(output, version);
    WriteU16(output, flags);
    WriteU16(output, headerMethod);
    WriteU16(output, 0);              // mod time
    WriteU16(output, DosDate1980);    // mod date
    WriteU32(output, crc);
    WriteU32(output, (uint)compSize);
    WriteU32(output, (uint)uncompSize);
    WriteU16(output, (ushort)nameBytes.Length);
    WriteU16(output, extraLen);       // размеры ≤ 2 ГиБ → ZIP64 в локальном не нужен; AES — extra 0x9901
    output.Write(nameBytes, 0, nameBytes.Length);

    if (encrypt)
      WriteAesExtra(output, method); // method = реальный (Store/Deflate)
  }

  private static void WriteCentralHeader(Stream output, CentralRecord r)
  {
    // Extra: ZIP64 (большое смещение) и/или WinZip-AES (шифрование).
    ushort extraLen = (ushort)((r.Zip64Offset ? 4 + 8 : 0) + (r.Encrypted ? AesExtraTotalSize : 0));
    ushort version = r.Encrypted ? VersionAes : (r.Zip64Offset ? VersionZip64 : VersionBase);
    ushort flags = r.Encrypted ? (ushort)(FlagUtf8 | FlagEncrypted) : FlagUtf8;
    ushort headerMethod = r.Encrypted ? WinZipAes.EncryptionMethod : r.Method;
    uint offset32 = r.Zip64Offset ? Zip64Sentinel32 : (uint)r.LocalOffset;

    WriteU32(output, CentralFileSignature);
    WriteU16(output, version);          // version made by
    WriteU16(output, version);          // version needed
    WriteU16(output, flags);
    WriteU16(output, headerMethod);
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

    if (r.Encrypted)
      WriteAesExtra(output, r.Method); // method = реальный (Store/Deflate)
  }

  // Пишет extra-поле WinZip-AES 0x9901: [id][size=7][version|"AE"|strength|actualMethod].
  private static void WriteAesExtra(Stream output, ushort actualMethod)
  {
    WriteU16(output, WinZipAes.ExtraFieldId);
    WriteU16(output, 7);
    byte[] data = WinZipAesMember.BuildExtraFieldData(WinZipAesMember.VersionAe1, WinZipAes.Strength.Aes256, actualMethod);
    output.Write(data, 0, data.Length);
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
      bool Zip64Offset,
      bool Encrypted);

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
