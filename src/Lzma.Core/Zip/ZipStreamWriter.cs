using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Deflate;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Потоковый писатель ZIP в seekable-<see cref="Stream"/> — без удержания всего архива в памяти.</para>
/// <para>
/// Файлы ≤ 2 ГиБ читаются по одному в память и сжимаются меньшим из Store(0)/Deflate(8) одноразовым
/// энкодером ПАРАЛЛЕЛЬНО волнами; файлы > 2 ГиБ пишутся потоково (Deflate, вход/выход не в памяти,
/// заголовок патчится seek-назад, при ≥ 4 ГиБ — ZIP64-размеры). Центральный каталог и EOCD
/// дописываются в конце. Смещения/счётчик 64-битные, при переполнении 4 ГиБ / 65535 записей пишется
/// ZIP64 (extra <c>0x0001</c> + ZIP64 EOCD), поэтому итоговый архив может быть больше 4 ГиБ. Имена —
/// UTF-8 (флаг bit 11).
/// </para>
/// <para>Шифрование члена больше 2 ГиБ пока не поддержано (нужен потоковый AES) —
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
      => Write(entries, output, int.MaxValue, Zip64SizeThreshold, progress, token, currentFile, maxDegreeOfParallelism, password);

  // Внутренняя перегрузка с настраиваемыми порогами: largeThreshold — с какого размера файл идёт
  // потоковым путём; zip64SizeThreshold — с какого размера local/central резервируют ZIP64-размеры.
  // Тесты понижают пороги, чтобы прогнать эти ветки на маленьких файлах без гигабайтных данных.
  internal static ZipWriteResult Write(
      IReadOnlyList<ZipStreamingEntry> entries,
      Stream output,
      long largeThreshold,
      long zip64SizeThreshold,
      IProgress<SevenZipProgress>? progress,
      CancellationToken token,
      IProgress<string>? currentFile,
      int maxDegreeOfParallelism,
      byte[]? password)
  {
    if (entries is null || output is null || !output.CanWrite || !output.CanSeek)
      return ZipWriteResult.InvalidData;

    bool encrypt = password is not null; // WinZip-AES (AES-256) для всех непустых членов

    foreach (ZipStreamingEntry e in entries)
    {
      if (e is null || string.IsNullOrEmpty(e.Name) || e.Name.Contains('\0'))
        return ZipWriteResult.InvalidData;

      if (!e.IsDirectory && e.Length > largeThreshold && encrypt)
        return ZipWriteResult.NotSupported; // шифрование члена > 2 ГиБ пока не поддержано (потоковый AES — отдельный шаг)
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

      // Большой файл (> 2 ГиБ) — отдельный потоковый путь (в память не читаем). Шифрование отложено:
      // такие члены отсеяны выше как NotSupported, поэтому здесь encrypt для большого файла невозможен.
      if (entries[i].Length > largeThreshold)
      {
        ZipStreamingEntry big = entries[i];
        currentFile?.Report(big.Name.Replace('\\', '/'));

        ZipWriteResult r = WriteLargeEntryStreaming(output, central, big, zip64SizeThreshold, token);
        if (r != ZipWriteResult.Ok)
          return r;

        processed += big.Length;
        progress?.Report(new SevenZipProgress(processed, total));
        i++;
        continue;
      }

      // Волна подряд идущих файлов ≤ 2 ГиБ: лимит по числу потоков и по памяти.
      int waveStart = i;
      long waveBytes = 0;
      while (i < entries.Count && !entries[i].IsDirectory && entries[i].Length <= largeThreshold)
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

    central.Add(new CentralRecord(nameBytes, method, crc, compSize, uncompSize, localOffset, isDir, zip64Offset, Zip64Sizes: false, encrypt));
  }

  private readonly record struct FileResult(ushort Method, uint Crc, byte[] Data);

  // Порог, с которого local header резервирует ZIP64-размеры: uncomp близок к 4 ГиБ (или compSize
  // из-за stored-накладных может перескочить сентинел). Запас 1 МиБ покрывает накладные stored.
  private const long Zip64SizeThreshold = (long)Zip64Sentinel32 - (1L << 20);

  /// <summary>
  /// Пишет большой файл (> 2 ГиБ) потоково: local header с заглушками CRC/compSize → потоковое
  /// сжатие Deflate прямо в выход (CRC входа и compSize считаются на лету) → seek назад и патч
  /// CRC/compSize. Выход seekable (проверено вызывающим). Метод всегда Deflate: на несжимаемых
  /// кусках энкодер сам падает в stored, так что compSize ≈ uncompSize + пренебрежимо малые накладные.
  /// </summary>
  private static ZipWriteResult WriteLargeEntryStreaming(
      Stream output, List<CentralRecord> central, ZipStreamingEntry e, long zip64SizeThreshold, CancellationToken token)
  {
    string name = e.Name.Replace('\\', '/');
    byte[] nameBytes = Encoding.UTF8.GetBytes(name);
    long uncompSize = e.Length;
    long localOffset = output.Position;
    bool zip64Sizes = uncompSize >= zip64SizeThreshold;
    bool zip64Offset = localOffset >= Zip64Sentinel32;
    const ushort method = MethodDeflate;

    // --- local header ---
    ushort version = zip64Sizes ? VersionZip64 : VersionBase;
    ushort extraLen = zip64Sizes ? (ushort)(4 + 16) : (ushort)0;

    WriteU32(output, LocalFileSignature);
    WriteU16(output, version);
    WriteU16(output, FlagUtf8);
    WriteU16(output, method);
    WriteU16(output, 0);           // mod time
    WriteU16(output, DosDate1980); // mod date
    WriteU32(output, 0);                                                     // crc — патчим после данных
    WriteU32(output, zip64Sizes ? Zip64Sentinel32 : 0);                      // compSize — патчим
    WriteU32(output, zip64Sizes ? Zip64Sentinel32 : (uint)uncompSize);       // uncompSize — известен
    WriteU16(output, (ushort)nameBytes.Length);
    WriteU16(output, extraLen);
    output.Write(nameBytes, 0, nameBytes.Length);

    if (zip64Sizes)
    {
      WriteU16(output, Zip64ExtraId);
      WriteU16(output, 16);             // uncomp(8) + comp(8)
      WriteU64(output, (ulong)uncompSize); // известен
      WriteU64(output, 0);                 // comp — патчим
    }

    long dataStart = output.Position;

    // --- потоковое сжатие ---
    uint crc;
    try
    {
      using Stream source = e.OpenRead();
      var crcSource = new Crc32ReadStream(source);
      DeflateEncoder.Encode(crcSource, uncompSize, output);
      if (crcSource.BytesRead != uncompSize)
        return ZipWriteResult.InvalidData; // источник короче заявленной длины
      crc = crcSource.Crc;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception)
    {
      return ZipWriteResult.InvalidData; // ошибка чтения источника
    }

    token.ThrowIfCancellationRequested();

    long dataEnd = output.Position;
    long compSize = dataEnd - dataStart;

    // --- патч CRC + compSize (seek назад, затем вернуть позицию в конец данных) ---
    output.Position = localOffset + 14;
    WriteU32(output, crc);

    if (zip64Sizes)
    {
      output.Position = dataStart - 8; // comp u64 — последние 8 байт перед данными
      WriteU64(output, (ulong)compSize);
    }
    else
    {
      output.Position = localOffset + 18;
      WriteU32(output, (uint)compSize);
    }

    output.Position = dataEnd;

    central.Add(new CentralRecord(nameBytes, method, crc, compSize, uncompSize, localOffset, false, zip64Offset, zip64Sizes, Encrypted: false));
    return ZipWriteResult.Ok;
  }

  // Read-through поток: считает CRC-32 прочитанных байт и их число. Позволяет получить CRC входа,
  // отдавая байты потоковому энкодеру без второго прохода.
  private sealed class Crc32ReadStream(Stream inner) : Stream
  {
    private uint _state = Crc32.InitialState;

    public uint Crc => Crc32.Finalize(_state);
    public long BytesRead { get; private set; }

    public override int Read(byte[] buffer, int offset, int count)
    {
      int n = inner.Read(buffer, offset, count);
      if (n > 0)
      {
        _state = Crc32.Update(_state, buffer.AsSpan(offset, n));
        BytesRead += n;
      }

      return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
  }

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
    // Extra: ZIP64 (большие размеры и/или смещение) и/или WinZip-AES (шифрование).
    // В ZIP64-extra поля идут строго в порядке: uncomp, comp, offset — только для тех, чьё 32-битное
    // поле в фиксированной части выставлено в сентинел.
    bool anyZip64 = r.Zip64Sizes || r.Zip64Offset;
    int zip64DataLen = (r.Zip64Sizes ? 16 : 0) + (r.Zip64Offset ? 8 : 0);
    ushort extraLen = (ushort)((anyZip64 ? 4 + zip64DataLen : 0) + (r.Encrypted ? AesExtraTotalSize : 0));
    ushort version = r.Encrypted ? VersionAes : (anyZip64 ? VersionZip64 : VersionBase);
    ushort flags = r.Encrypted ? (ushort)(FlagUtf8 | FlagEncrypted) : FlagUtf8;
    ushort headerMethod = r.Encrypted ? WinZipAes.EncryptionMethod : r.Method;
    uint offset32 = r.Zip64Offset ? Zip64Sentinel32 : (uint)r.LocalOffset;
    uint comp32 = r.Zip64Sizes ? Zip64Sentinel32 : (uint)r.CompSize;
    uint uncomp32 = r.Zip64Sizes ? Zip64Sentinel32 : (uint)r.UncompSize;

    WriteU32(output, CentralFileSignature);
    WriteU16(output, version);          // version made by
    WriteU16(output, version);          // version needed
    WriteU16(output, flags);
    WriteU16(output, headerMethod);
    WriteU16(output, 0);               // mod time
    WriteU16(output, DosDate1980);     // mod date
    WriteU32(output, r.Crc);
    WriteU32(output, comp32);
    WriteU32(output, uncomp32);
    WriteU16(output, (ushort)r.NameBytes.Length);
    WriteU16(output, extraLen);
    WriteU16(output, 0);               // comment length
    WriteU16(output, 0);               // disk number start
    WriteU16(output, 0);               // internal attributes
    WriteU32(output, r.IsDirectory ? DosAttrDirectory : DosAttrArchive);
    WriteU32(output, offset32);
    output.Write(r.NameBytes, 0, r.NameBytes.Length);

    if (anyZip64)
    {
      WriteU16(output, Zip64ExtraId);
      WriteU16(output, (ushort)zip64DataLen);
      if (r.Zip64Sizes)
      {
        WriteU64(output, (ulong)r.UncompSize);
        WriteU64(output, (ulong)r.CompSize);
      }

      if (r.Zip64Offset)
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
      bool Zip64Sizes,
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
