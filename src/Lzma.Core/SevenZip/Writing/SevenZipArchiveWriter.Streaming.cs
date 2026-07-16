using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

/// <summary>Элемент потокового создания архива: данные берутся из <see cref="Stream"/> по требованию,
/// а не из <c>byte[]</c> в памяти — это позволяет паковать файлы больше 2 ГиБ.</summary>
/// <param name="Name">Имя записи (путь с '/').</param>
/// <param name="Length">Размер данных в байтах (для каталога/пустого файла — 0).</param>
/// <param name="OpenRead">Открывает читаемый поток данных (вызывается только для непустых файлов).</param>
public sealed record SevenZipStreamingEntry(
    string Name,
    long Length,
    Func<Stream> OpenRead,
    bool IsDirectory = false,
    uint? WindowsAttributes = null,
    DateTime? LastWriteTimeUtc = null);

// Потоковая запись .7z в Stream: сжатые данные каждого файла льются прямо в выходной поток
// (через Lzma2LzmaEncoder.EncodeStreaming), размеры — long/ulong, next-header строится в памяти
// (он мал), сигнатура патчится в конце (нужен seekable output). Не держит архив/файлы в памяти.
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит LZMA2-архив, записывая его потоково в <paramref name="output"/> (seekable). Каждый
  /// непустой файл открывается через <see cref="SevenZipStreamingEntry.OpenRead"/> и сжимается на
  /// лету; ни весь файл, ни весь архив в памяти не держатся.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildLzma2ArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      int dictionarySize,
      int maxDegreeOfParallelism = 0,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(output);

    if (!output.CanWrite || !output.CanSeek)
      return SevenZipArchiveWriteResult.NotSupported; // патч сигнатуры требует seek

    if (dictionarySize <= 0)
      return SevenZipArchiveWriteResult.InvalidData;

    if (!Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out Lzma2Properties properties))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!properties.TryGetDictionarySizeInt32(out int effectiveDictionarySize))
      return SevenZipArchiveWriteResult.NotSupported;

    SevenZipArchiveWriteResult validation = ValidateStreamingEntries(entries);
    if (validation != SevenZipArchiveWriteResult.Ok)
      return validation;

    var lzmaProperties = new LzmaProperties(3, 0, 2);
    byte[] coderBytes = [0x21, Lzma2MethodId, 0x01, properties.DictionaryProp];

    long startPos = output.Position;

    // Резервируем место под сигнатуру (пропатчим в конце).
    output.Write(new byte[SevenZipSignatureHeader.Size]);

    int count = 0;
    long totalContent = 0;
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
      {
        count++;
        totalContent += entries[i].Length;
      }

    var packSizes = new ulong[count];
    var unpackSizes = new ulong[count];
    var crcs = new uint[count];

    progress?.Report(new SevenZipProgress(0, totalContent));
    long processed = 0;

    // Индексы data-entry в порядке архива (packed-стримы пишутся строго в этом порядке).
    var dataOrder = new List<int>(count);
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
        dataOrder.Add(i);

    // Файл <= размера блока сжимается ОДНИМ блоком (Encode) — байт-в-байт как блочно-параллельный
    // путь для одноблочного файла. Такие файлы жмём ПАРАЛЛЕЛЬНО МЕЖДУ СОБОЙ (волнами), а пишем по
    // порядку. Файлы больше блока — блочно-параллельно напрямую в output (внутри-файловый параллелизм).
    int blockSize = Math.Max(effectiveDictionarySize, 1 << 20);
    int dop = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount;
    const long waveMemoryLimit = 128L << 20; // ограничение памяти на волну
    var parallelOptions = new ParallelOptions
    {
      MaxDegreeOfParallelism = dop,
      CancellationToken = token,
    };

    int di = 0;
    while (di < dataOrder.Count)
    {
      token.ThrowIfCancellationRequested();

      SevenZipStreamingEntry entry = entries[dataOrder[di]];

      if (entry.Length > blockSize)
      {
        currentFile?.Report(new SevenZipCompressionFileProgress(entry.Name, "LZMA2"));

        // Большой файл — блочно-параллельно напрямую в output (с прогрессом внутри файла).
        long processedBefore = processed;
        IProgress<long>? fileProgress = progress is null ? null
            : new LongProgressAdapter(local => progress.Report(
                new SevenZipProgress(Math.Min(processedBefore + local, totalContent), totalContent)));

        long packSize;
        uint crc;
        using (Stream source = entry.OpenRead())
          packSize = Lzma2LzmaEncoder.EncodeParallelToStream(
              source, entry.Length, lzmaProperties, effectiveDictionarySize, output,
              out crc, maxDegreeOfParallelism: dop, bytesProgress: fileProgress, token: token);

        packSizes[di] = (ulong)packSize;
        unpackSizes[di] = (ulong)entry.Length;
        crcs[di] = crc;

        processed += entry.Length;
        progress?.Report(new SevenZipProgress(processed, totalContent));
        di++;
        continue;
      }

      // Собираем волну подряд идущих мелких файлов (<= размера блока), с лимитом памяти/потоков.
      int waveStart = di;
      long waveBytes = 0;
      while (di < dataOrder.Count)
      {
        SevenZipStreamingEntry e = entries[dataOrder[di]];
        if (e.Length > blockSize)
          break;

        int waveCount = di - waveStart;
        if (waveCount >= dop || (waveCount > 0 && waveBytes + e.Length > waveMemoryLimit))
          break;

        waveBytes += e.Length;
        di++;
      }

      int n = di - waveStart;
      var compressed = new byte[n][];
      var waveCrcs = new uint[n];

      // Сжимаем файлы волны ПАРАЛЛЕЛЬНО (каждый — одним Encode, детерминировано).
      Parallel.For(0, n, parallelOptions, k =>
      {
        SevenZipStreamingEntry e = entries[dataOrder[waveStart + k]];
        byte[] data = ReadExactlyToArray(e.OpenRead(), (int)e.Length);
        waveCrcs[k] = Crc32.Compute(data);
        compressed[k] = Lzma2LzmaEncoder.Encode(data, lzmaProperties, effectiveDictionarySize);
      });

      // Пишем сжатые буферы СТРОГО по порядку.
      for (int k = 0; k < n; k++)
      {
        SevenZipStreamingEntry e = entries[dataOrder[waveStart + k]];
        currentFile?.Report(new SevenZipCompressionFileProgress(e.Name, "LZMA2"));
        output.Write(compressed[k], 0, compressed[k].Length);

        packSizes[waveStart + k] = (ulong)compressed[k].Length;
        unpackSizes[waveStart + k] = (ulong)e.Length;
        crcs[waveStart + k] = waveCrcs[k];

        processed += e.Length;
        progress?.Report(new SevenZipProgress(processed, totalContent));
      }
    }

    return FinalizeStreamingArchive(entries, output, startPos, coderBytes, packSizes, unpackSizes, crcs);
  }

  /// <summary>
  /// ПОТОКОВОЕ создание архива методом, который сжимает КАЖДЫЙ файл целиком (PPMd, Copy): файл
  /// читается в память (&lt;= 2 ГиБ) по одному, сжимается делегатом и пишется в <paramref name="output"/>
  /// — не держим весь набор файлов в памяти. Последовательно (без параллелизма — для PPMd это
  /// обязательно). <paramref name="coderBytes"/> — байты coder-а для next-header.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildPerFileStreamingArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      Func<byte[], StreamingEncodedFile?> encodeFile,
      IProgress<SevenZipProgress>? progress,
      System.Threading.CancellationToken token,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(output);

    if (!output.CanWrite || !output.CanSeek)
      return SevenZipArchiveWriteResult.NotSupported;

    SevenZipArchiveWriteResult validation = ValidateStreamingEntries(entries);
    if (validation != SevenZipArchiveWriteResult.Ok)
      return validation;

    long startPos = output.Position;
    output.Write(new byte[SevenZipSignatureHeader.Size]);

    int count = 0;
    long totalContent = 0;
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
      {
        count++;
        totalContent += entries[i].Length;
      }

    // packSizes — ПЛОСКИЙ список по всем folder-ам (folder может дать несколько packed-стримов: BCJ2 — 4).
    var flatPackSizes = new List<ulong>(count);
    var folderBodies = new byte[count][];
    var coderUnpackSizes = new ulong[count][];
    var crcs = new uint[count];

    progress?.Report(new SevenZipProgress(0, totalContent));
    long processed = 0;
    int folder = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry entry = entries[i];
      if (!IsStreamingDataEntry(entry))
        continue;

      token.ThrowIfCancellationRequested();

      // Пофайловое сжатие держит файл в памяти целиком — > 2 ГиБ на файл пока не поддерживаем.
      if (entry.Length > int.MaxValue)
        return SevenZipArchiveWriteResult.NotSupported;

      byte[] data = ReadExactlyToArray(entry.OpenRead(), (int)entry.Length);
      uint crc = Crc32.Compute(data);

      if (encodeFile(data) is not { } enc)
        return SevenZipArchiveWriteResult.InternalError;

      currentFile?.Report(new SevenZipCompressionFileProgress(entry.Name, enc.Codec));

      // Packed-стримы folder-а пишем строго в порядке его packed-индексов.
      for (int s = 0; s < enc.PackedStreams.Length; s++)
      {
        byte[] packed = enc.PackedStreams[s];
        output.Write(packed, 0, packed.Length);
        flatPackSizes.Add((ulong)packed.Length);
      }

      folderBodies[folder] = enc.FolderBody;
      coderUnpackSizes[folder] = enc.CoderUnpackSizes;
      crcs[folder] = crc;
      folder++;

      processed += entry.Length;
      progress?.Report(new SevenZipProgress(processed, totalContent));
    }

    return FinalizeStreamingArchiveMultiFolder(entries, output, startPos, folderBodies, [.. flatPackSizes], coderUnpackSizes, crcs);
  }

  // Байты PPMd-coder-а (7z): flags 0x23 | method id 03 04 01 | props(order, memSize LE).
  private static byte[] PpmdCoderBytes() =>
  [
      0x23,
      0x03, 0x04, 0x01,
      0x05,
      (byte)PpmdOrder,
      (byte)(PpmdMemSize & 0xFF),
      (byte)((PpmdMemSize >> 8) & 0xFF),
      (byte)((PpmdMemSize >> 16) & 0xFF),
      (byte)((PpmdMemSize >> 24) & 0xFF),
  ];

  /// <summary>Потоковое создание PPMd-архива (пофайлово, без загрузки всего набора в память).</summary>
  public static SevenZipArchiveWriteResult BuildPpmdArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    byte[] coderBytes = PpmdCoderBytes();
    return BuildPerFileStreamingArchiveToStream(entries, output,
        data => SingleCoderEncoded(EncodePpmd(data), coderBytes, data.Length, "PPMd"), progress, token, currentFile);
  }

  /// <summary>Потоковое создание Copy-архива (без сжатия; пофайлово, не держим весь набор в памяти).</summary>
  public static SevenZipArchiveWriteResult BuildCopyArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    // Copy coder: flags = idSize(1) | без атрибутов = 0x01, method id = 0x00.
    byte[] coderBytes = [0x01, 0x00];
    return BuildPerFileStreamingArchiveToStream(entries, output,
        data => SingleCoderEncoded(data, coderBytes, data.Length, "Copy"), progress, token, currentFile);
  }

  /// <summary>
  /// Потоковое создание архива с АВТОВЫБОРОМ кодека ПОФАЙЛОВО: для каждого файла эвристика по доле
  /// «бинарных» байт выбирает PPMd (текст — плотнее) или LZMA2. Пофайлово, без загрузки набора в
  /// память (файл &lt;= 2 ГиБ). У каждого folder-а свой coder (PPMd или LZMA2).
  /// </summary>
  public static SevenZipArchiveWriteResult BuildAutoArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      int dictionarySize,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    if (dictionarySize <= 0)
      return SevenZipArchiveWriteResult.InvalidData;

    if (!Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out Lzma2Properties properties))
      return SevenZipArchiveWriteResult.InvalidData;

    if (!properties.TryGetDictionarySizeInt32(out int effectiveDictionarySize))
      return SevenZipArchiveWriteResult.NotSupported;

    var lzmaProperties = new LzmaProperties(3, 0, 2);
    byte[] lzma2Coder = [0x21, Lzma2MethodId, 0x01, properties.DictionaryProp];
    byte[] ppmdCoder = PpmdCoderBytes();
    byte[] copyCoder = [0x01, 0x00];

    return BuildPerFileStreamingArchiveToStream(entries, output, data =>
    {
      return ChooseAutoMethodForBytes(data) switch
      {
        SevenZipWriterCompressionMethod.Ppmd => SingleCoderEncoded(EncodePpmd(data), ppmdCoder, data.Length, "PPMd"),
        SevenZipWriterCompressionMethod.Copy => SingleCoderEncoded(data, copyCoder, data.Length, "Copy"),
        SevenZipWriterCompressionMethod.Bcj2 => EncodeBcj2Streaming(data),
        _ => SingleCoderEncoded(Lzma2LzmaEncoder.Encode(data, lzmaProperties, effectiveDictionarySize), lzma2Coder, data.Length, "LZMA2"),
      };
    }, progress, token, currentFile);
  }

  // Пофайловое BCJ2-кодирование для потокового folder-а: 4 coder-а (3×LZMA2 на Main/Call/Jump +
  // BCJ2), Control — сырой packed-стрим. Тело folder-а и порядок стримов — как в in-memory BCJ2-пути.
  private static StreamingEncodedFile? EncodeBcj2Streaming(byte[] data)
  {
    SevenZipBcj2Streams s = SevenZipBcj2Encoder.Encode(data);

    if (!TryLzma2Compress(s.Main, out byte[] packedMain, out byte mainProp) ||
        !TryLzma2Compress(s.Call, out byte[] packedCall, out byte callProp) ||
        !TryLzma2Compress(s.Jump, out byte[] packedJump, out byte jumpProp))
      return null;

    byte[] folderBody = BuildBcj2Lzma2FolderBody(mainProp, callProp, jumpProp);

    // Порядок packed-стримов = packed-индексы folder-а [0,1,2,6]: lzma2(main/call/jump) + control сырой.
    // CodersUnpackSize по coder-ам: LZMA2(main)=Main, LZMA2(call)=Call, LZMA2(jump)=Jump, BCJ2=файл.
    return new StreamingEncodedFile(
        [packedMain, packedCall, packedJump, s.Control],
        folderBody,
        [(ulong)s.Main.Length, (ulong)s.Call.Length, (ulong)s.Jump.Length, (ulong)data.Length],
        "BCJ2");
  }

  // Пофайловая эвристика автовыбора (по префиксу-сэмплу): практически несжимаемые данные
  // (высокая энтропия — уже сжато/медиа/шифр/случайные) → Copy (хранить); преимущественно
  // текстовые (мало «бинарных» байт) → PPMd; остальное → LZMA2.
  internal static SevenZipWriterCompressionMethod ChooseAutoMethodForBytes(byte[] data)
  {
    if (data.Length == 0)
      return SevenZipWriterCompressionMethod.Lzma2;

    // x86/x64 PE-исполняемые → BCJ2 (адреса ветвлений становятся абсолютными и лучше сжимаются).
    if (LooksLikeX86Executable(data))
      return SevenZipWriterCompressionMethod.Bcj2;

    int sample = data.Length <= AutoSampleBytes ? data.Length : AutoSampleBytes;

    Span<int> histogram = stackalloc int[256];
    long binary = 0;
    for (int i = 0; i < sample; i++)
    {
      byte b = data[i];
      histogram[b]++;
      if (IsBinaryByte(b))
        binary++;
    }

    // Энтропия Шеннона по сэмплу (бит/байт): 0..8.
    double entropy = 0.0;
    for (int s = 0; s < 256; s++)
    {
      int c = histogram[s];
      if (c == 0)
        continue;
      double p = (double)c / sample;
      entropy -= p * Math.Log2(p);
    }

    if (entropy >= AutoIncompressibleEntropyBitsPerByte)
      return SevenZipWriterCompressionMethod.Copy;

    return binary < sample * AutoBinaryByteThreshold
        ? SevenZipWriterCompressionMethod.Ppmd
        : SevenZipWriterCompressionMethod.Lzma2;
  }

  // Общая валидация записей потокового создания.
  private static SevenZipArchiveWriteResult ValidateStreamingEntries(IReadOnlyList<SevenZipStreamingEntry> entries)
  {
    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry e = entries[i];
      if (e is null || e.Name is null || e.Length < 0)
        return SevenZipArchiveWriteResult.InvalidData;
      if (!IsSupportedEntryPath(e.Name))
        return SevenZipArchiveWriteResult.InvalidData;
      if (e.IsDirectory && e.Length != 0)
        return SevenZipArchiveWriteResult.InvalidData;
      if (!e.IsDirectory && e.Length > 0 && e.OpenRead is null)
        return SevenZipArchiveWriteResult.InvalidData;
    }

    return SevenZipArchiveWriteResult.Ok;
  }

  // Общая финализация (один coder на все folder-ы): разворачивает coder в per-folder массив.
  private static SevenZipArchiveWriteResult FinalizeStreamingArchive(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      long startPos,
      byte[] coderBytes,
      ulong[] packSizes,
      ulong[] unpackSizes,
      uint[] crcs)
  {
    var perFolder = new byte[packSizes.Length][];
    for (int i = 0; i < perFolder.Length; i++)
      perFolder[i] = coderBytes;

    return FinalizeStreamingArchive(entries, output, startPos, perFolder, packSizes, unpackSizes, crcs);
  }

  // Финализация: ОДИН coder на каждый folder (LZMA2/PPMd/Copy/Auto). Оборачивает coder в тело
  // folder-а (numCoders=1) и один packed-stream/один размер выхода на folder → общий multi-folder.
  private static SevenZipArchiveWriteResult FinalizeStreamingArchive(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      long startPos,
      byte[][] coderBytesPerFolder,
      ulong[] packSizes,
      ulong[] unpackSizes,
      uint[] crcs)
  {
    var folderBodies = new byte[coderBytesPerFolder.Length][];
    var coderUnpackSizes = new ulong[coderBytesPerFolder.Length][];
    for (int i = 0; i < coderBytesPerFolder.Length; i++)
    {
      folderBodies[i] = WrapSingleCoderFolderBody(coderBytesPerFolder[i]);
      coderUnpackSizes[i] = [unpackSizes[i]];
    }

    return FinalizeStreamingArchiveMultiFolder(entries, output, startPos, folderBodies, packSizes, coderUnpackSizes, crcs);
  }

  // Оборачивает байты одного coder-а в тело folder-а: numCoders=1 + coder (один packed-stream, без bind pairs).
  private static byte[] WrapSingleCoderFolderBody(byte[] coderBytes)
  {
    var body = new List<byte>(1 + coderBytes.Length);
    TryWriteUInt64(body, 1);
    body.AddRange(coderBytes);
    return [.. body];
  }

  // Результат пофайлового кодирования для потокового folder-а: packed-стримы (в порядке folder-а),
  // готовое тело folder-а (numCoders + coder-ы + bind pairs + packed-индексы) и размеры выходов
  // coder-ов. Одно-coder кодек → 1 packed-стрим; BCJ2 → 4 (main/call/jump/control).
  private readonly record struct StreamingEncodedFile(
      byte[][] PackedStreams, byte[] FolderBody, ulong[] CoderUnpackSizes, string Codec);

  // Одно-coder результат: один packed-стрим, тело folder-а numCoders=1, один размер выхода.
  private static StreamingEncodedFile SingleCoderEncoded(byte[] packed, byte[] coderBytes, long unpackSize, string codec)
      => new([packed], WrapSingleCoderFolderBody(coderBytes), [(ulong)unpackSize], codec);

  // Общая финализация ЛЮБЫХ folder-ов (в т.ч. много-coder/много-стрим, напр. BCJ2): готовые тела
  // folder-ов + ПЛОСКИЙ packSizes (по всем folder-ам) + размеры выходов coder-ов на folder + CRC.
  // Синтетические entries для FilesInfo + next-header + патч сигнатуры.
  private static SevenZipArchiveWriteResult FinalizeStreamingArchiveMultiFolder(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      long startPos,
      byte[][] folderBodies,
      ulong[] packSizes,
      ulong[][] coderUnpackSizes,
      uint[] crcs)
  {
    var synthetic = new SevenZipArchiveWriterEntry[entries.Count];
    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry e = entries[i];
      byte[] marker = IsStreamingDataEntry(e) ? [0] : [];
      synthetic[i] = new SevenZipArchiveWriterEntry(e.Name, marker, e.IsDirectory, e.WindowsAttributes, e.LastWriteTimeUtc);
    }

    if (!TryBuildStreamingNextHeader(synthetic, packSizes, coderUnpackSizes, crcs, folderBodies, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    long packedEnd = output.Position;
    long nextHeaderOffset = packedEnd - (startPos + SevenZipSignatureHeader.Size);

    output.Write(nextHeaderBytes);

    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);
    var signature = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)nextHeaderOffset,
        NextHeaderSize: (ulong)nextHeaderBytes.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] signatureBytes = new byte[SevenZipSignatureHeader.Size];
    signature.Write(signatureBytes);

    long endPos = output.Position;
    output.Position = startPos;
    output.Write(signatureBytes);
    output.Position = endPos;
    output.Flush();

    return SevenZipArchiveWriteResult.Ok;
  }

  private static bool IsStreamingDataEntry(SevenZipStreamingEntry e)
      => !e.IsDirectory && e.Length > 0;

  // Читает ровно length байт из потока в массив и закрывает поток (для параллельного сжатия мелких файлов).
  private static byte[] ReadExactlyToArray(Stream source, int length)
  {
    using (source)
    {
      byte[] buffer = new byte[length];
      int offset = 0;
      while (offset < length)
      {
        int n = source.Read(buffer, offset, length - offset);
        if (n <= 0)
          throw new EndOfStreamException("Входной файл короче заявленной длины.");
        offset += n;
      }

      return buffer;
    }
  }

  // Строит next-header для потокового LZMA2-сценария: PackInfo/UnpackInfo с ulong-размерами
  // (поддержка >2 ГБ), FilesInfo — через существующие writer-ы (по синтетическим entries).
  private static bool TryBuildStreamingNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> syntheticEntries,
      ulong[] packSizes,
      ulong[][] coderUnpackSizes,
      uint[] unpackCrcs,
      byte[][] folderBodies,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteStreamingPackInfo(header, packSizes))
      return false;

    if (!TryWriteStreamingFoldersUnpackInfo(header, folderBodies, coderUnpackSizes, unpackCrcs))
      return false;

    header.Add(SevenZipNid.End);

    if (AllEntriesAreNonEmptyFiles(syntheticEntries))
    {
      if (!TryWriteAllNonEmptyCopyEntriesFilesInfo(header, syntheticEntries))
        return false;
    }
    else if (!TryWriteMixedCopyEntriesFilesInfo(header, syntheticEntries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];
    return true;
  }

  // PackInfo с ulong pack-размерами (twin TryWriteCompressedStreamsPackInfo на long/ulong).
  private static bool TryWriteStreamingPackInfo(List<byte> header, ulong[] packSizes)
  {
    header.Add(SevenZipNid.PackInfo);

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, (ulong)packSizes.Length))
      return false;

    header.Add(SevenZipNid.Size);

    for (int i = 0; i < packSizes.Length; i++)
      if (!TryWriteUInt64(header, packSizes[i]))
        return false;

    header.Add(SevenZipNid.End);
    return true;
  }

  // UnpackInfo с ulong-размерами: у каждого folder-а СВОЁ готовое тело (folderBodies[i] уже содержит
  // numCoders + coder-ы + bind pairs + packed-индексы) и свои размеры выходов coder-ов
  // (coderUnpackSizes[i][j]). Поддерживает и одно-coder folder-ы (LZMA2/PPMd/Copy), и много-coder
  // (BCJ2: 3×LZMA2 + BCJ2). CRC — по одному на folder (финальный выход).
  private static bool TryWriteStreamingFoldersUnpackInfo(
      List<byte> header, byte[][] folderBodies, ulong[][] coderUnpackSizes, uint[] unpackCrcs)
  {
    if (folderBodies.Length != coderUnpackSizes.Length || folderBodies.Length != unpackCrcs.Length)
      return false;

    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)folderBodies.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < folderBodies.Length; i++)
      header.AddRange(folderBodies[i]);

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < coderUnpackSizes.Length; i++)
      for (int j = 0; j < coderUnpackSizes[i].Length; j++)
        if (!TryWriteUInt64(header, coderUnpackSizes[i][j]))
          return false;

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, unpackCrcs);

    header.Add(SevenZipNid.End);
    return true;
  }

  // Синхронный IProgress<long> из делегата (отчёты идут на потоке энкодера, не через SynchronizationContext).
  private sealed class LongProgressAdapter(Action<long> report) : IProgress<long>
  {
    public void Report(long value) => report(value);
  }
}
