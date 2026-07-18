using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// SOLID-запись 7z: файлы одной группы склеиваются и сжимаются ОДНИМ coder-ом в общий folder с
// несколькими под-потоками (SubStreamsInfo). Для PPMd/LZMA2 это даёт лучший коэффициент, чем
// пофайлово (модель статистики копится по всей группе). Пока: одна группа = один folder ≤ 2 ГиБ
// суммарно (одноразовый Encode держит всю склейку в памяти).
public static partial class SevenZipArchiveWriter
{
  // Размер одного solid-блока по умолчанию: файлы кодек-группы режутся на блоки ≤ этого лимита
  // (память + параллелизм); внутри блока — solid (статистика копится). Насыщение PPMd/окна LZMA2
  // наступает заметно раньше 32 МиБ, поэтому потеря плотности от нарезки минимальна.
  private const long DefaultSolidBlockSize = 32L << 20;

  // Бюджет памяти на волну параллельно жмущихся блоков (ограничивает пик памяти).
  private const long SolidWaveMemoryBudget = 256L << 20;

  /// <summary>
  /// Потоковое создание 7z с АВТОВЫБОРОМ кодека и SOLID-группировкой: файлы классифицируются по
  /// содержимому (PPMd/LZMA2/Copy), файлы одного кодека склеиваются в solid-блоки (≤ ~32 МиБ) —
  /// модель копит статистику по группе (плотнее пофайлового), а блоки жмутся ПАРАЛЛЕЛЬНО между собой.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildAutoSolidArchiveToStream(
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
      return SevenZipArchiveWriteResult.NotSupported;

    SevenZipArchiveWriteResult validation = ValidateStreamingEntries(entries);
    if (validation != SevenZipArchiveWriteResult.Ok)
      return validation;

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

    // Кодек → (байты coder-а, энкодер, имя). Энкодеры потокобезопасны (свежее состояние на вызов).
    Func<byte[], byte[]> ResolveEncode(SevenZipWriterCompressionMethod codec) => codec switch
    {
      SevenZipWriterCompressionMethod.Ppmd => EncodePpmd,
      SevenZipWriterCompressionMethod.Copy => data => data,
      _ => data => Lzma2LzmaEncoder.Encode(data, lzmaProperties, effectiveDictionarySize),
    };
    byte[] ResolveCoder(SevenZipWriterCompressionMethod codec) => codec switch
    {
      SevenZipWriterCompressionMethod.Ppmd => ppmdCoder,
      SevenZipWriterCompressionMethod.Copy => copyCoder,
      _ => lzma2Coder,
    };
    static string CodecName(SevenZipWriterCompressionMethod codec) => codec switch
    {
      SevenZipWriterCompressionMethod.Ppmd => "PPMd",
      SevenZipWriterCompressionMethod.Copy => "Copy",
      _ => "LZMA2",
    };

    // Индексы файлов с данными + предвалидация размера (файл держим в памяти при solid-склейке).
    var dataOrder = new List<int>();
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
      {
        if (entries[i].Length > int.MaxValue)
          return SevenZipArchiveWriteResult.NotSupported;
        dataOrder.Add(i);
      }

    // Классификация по сэмплу (≤1 МиБ) — одно дешёвое чтение начала файла.
    var byCodec = new Dictionary<SevenZipWriterCompressionMethod, List<int>>
    {
      [SevenZipWriterCompressionMethod.Ppmd] = [],
      [SevenZipWriterCompressionMethod.Lzma2] = [],
      [SevenZipWriterCompressionMethod.Copy] = [],
    };

    foreach (int idx in dataOrder)
    {
      token.ThrowIfCancellationRequested();
      SevenZipStreamingEntry e = entries[idx];
      byte[] sample = ReadSample(e, (int)Math.Min(e.Length, 1 << 20));
      SevenZipWriterCompressionMethod codec = ChooseAutoMethodForBytes(sample);
      if (!byCodec.TryGetValue(codec, out List<int>? list))
        list = byCodec[SevenZipWriterCompressionMethod.Lzma2];
      list.Add(idx);
    }

    // Блоки в фиксированном порядке кодеков; внутри кодека режем по лимиту размера.
    var blocks = new List<(SevenZipWriterCompressionMethod Codec, int[] Indices, long Bytes)>();
    foreach (SevenZipWriterCompressionMethod codec in (ReadOnlySpan<SevenZipWriterCompressionMethod>)
             [SevenZipWriterCompressionMethod.Ppmd, SevenZipWriterCompressionMethod.Lzma2, SevenZipWriterCompressionMethod.Copy])
    {
      List<int> list = byCodec[codec];
      int j = 0;
      while (j < list.Count)
      {
        var block = new List<int>();
        long bytes = 0;
        while (j < list.Count && (block.Count == 0 || bytes + entries[list[j]].Length <= DefaultSolidBlockSize))
        {
          bytes += entries[list[j]].Length;
          block.Add(list[j]);
          j++;
        }
        blocks.Add((codec, [.. block], bytes));
      }
    }

    long totalContent = 0;
    foreach (var b in blocks)
      totalContent += b.Bytes;

    long startPos = output.Position;
    output.Write(new byte[SevenZipSignatureHeader.Size]);

    int nb = blocks.Count;
    var folderBodies = new byte[nb][];
    var packSizes = new ulong[nb];
    var coderUnpackSizes = new ulong[nb][];
    var numStreams = new int[nb];
    var fileSizes = new long[nb][];
    var fileCrcs = new uint[nb][];

    int dop = maxDegreeOfParallelism > 0 ? maxDegreeOfParallelism : Environment.ProcessorCount;
    var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = dop, CancellationToken = token };

    progress?.Report(new SevenZipProgress(0, totalContent));
    long processed = 0;

    int bi = 0;
    while (bi < nb)
    {
      token.ThrowIfCancellationRequested();

      // Волна блоков: лимит по числу потоков и по бюджету памяти.
      int waveStart = bi;
      long waveBytes = 0;
      while (bi < nb)
      {
        int waveCount = bi - waveStart;
        if (waveCount >= dop || (waveCount > 0 && waveBytes + blocks[bi].Bytes > SolidWaveMemoryBudget))
          break;
        waveBytes += blocks[bi].Bytes;
        bi++;
      }

      int n = bi - waveStart;
      var results = new SolidBlockResult[n];

      try
      {
        Parallel.For(0, n, parallelOptions, k =>
        {
          var (codec, indices, _) = blocks[waveStart + k];
          results[k] = EncodeSolidBlock(entries, indices, ResolveEncode(codec), ResolveCoder(codec));
        });
      }
      catch (AggregateException ex)
      {
        foreach (Exception inner in ex.Flatten().InnerExceptions)
          if (inner is OperationCanceledException)
            throw new OperationCanceledException(token);

        return SevenZipArchiveWriteResult.InternalError;
      }

      // Пишем блоки-folder-ы СТРОГО по порядку волны.
      for (int k = 0; k < n; k++)
      {
        int f = waveStart + k;
        var (codec, indices, _) = blocks[f];
        SolidBlockResult res = results[k];

        foreach (int idx in indices)
          currentFile?.Report(new SevenZipCompressionFileProgress(entries[idx].Name, CodecName(codec)));

        output.Write(res.Packed, 0, res.Packed.Length);

        folderBodies[f] = res.FolderBody;
        packSizes[f] = (ulong)res.Packed.Length;
        coderUnpackSizes[f] = [(ulong)res.TotalUnpack];
        numStreams[f] = res.FileSizes.Length;
        fileSizes[f] = res.FileSizes;
        fileCrcs[f] = res.FileCrcs;

        processed += res.TotalUnpack;
        progress?.Report(new SevenZipProgress(processed, totalContent));
      }
    }

    // Порядок FilesInfo: сначала data-файлы в порядке блоков (= порядок под-потоков), затем пустые/dirs.
    var reordered = new List<SevenZipStreamingEntry>(entries.Count);
    foreach (var b in blocks)
      foreach (int idx in b.Indices)
        reordered.Add(entries[idx]);
    for (int i = 0; i < entries.Count; i++)
      if (!IsStreamingDataEntry(entries[i]))
        reordered.Add(entries[i]);

    return FinalizeStreamingArchiveSolid(
        reordered, output, startPos, folderBodies, packSizes, coderUnpackSizes, numStreams, fileSizes, fileCrcs);
  }

  // Сжимает один solid-блок: читает и склеивает файлы, кодирует, считает per-file размеры/CRC.
  private static SolidBlockResult EncodeSolidBlock(
      IReadOnlyList<SevenZipStreamingEntry> entries, int[] indices, Func<byte[], byte[]> encode, byte[] coderBytes)
  {
    long total = 0;
    foreach (int idx in indices)
      total += entries[idx].Length;

    byte[] concat = new byte[(int)total];
    var sizes = new long[indices.Length];
    var crcs = new uint[indices.Length];
    int pos = 0;

    for (int k = 0; k < indices.Length; k++)
    {
      SevenZipStreamingEntry e = entries[indices[k]];
      byte[] data = ReadExactlyToArray(e.OpenRead(), (int)e.Length);
      sizes[k] = data.Length;
      crcs[k] = Crc32.Compute(data);
      Array.Copy(data, 0, concat, pos, data.Length);
      pos += data.Length;
    }

    byte[] packed = encode(concat);
    return new SolidBlockResult(WrapSingleCoderFolderBody(coderBytes), packed, total, sizes, crcs);
  }

  private readonly record struct SolidBlockResult(byte[] FolderBody, byte[] Packed, long TotalUnpack, long[] FileSizes, uint[] FileCrcs);

  // Читает первые sampleLength байт файла (для классификации), закрывая поток.
  private static byte[] ReadSample(SevenZipStreamingEntry e, int sampleLength)
  {
    if (sampleLength <= 0)
      return [];

    using Stream source = e.OpenRead();
    byte[] buffer = new byte[sampleLength];
    int offset = 0;
    while (offset < sampleLength)
    {
      int read = source.Read(buffer, offset, sampleLength - offset);
      if (read <= 0)
        break;
      offset += read;
    }

    return offset == sampleLength ? buffer : buffer[..offset];
  }

  /// <summary>Solid-архив LZMA2: все файлы в одном LZMA2-потоке (модель копит статистику по группе).</summary>
  public static SevenZipArchiveWriteResult BuildLzma2SolidArchiveToStream(
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
    byte[] coder = [0x21, Lzma2MethodId, 0x01, properties.DictionaryProp];

    return BuildSolidArchiveToStream(
        entries, output,
        data => Lzma2LzmaEncoder.Encode(data, lzmaProperties, effectiveDictionarySize),
        coder, "LZMA2", progress, token, currentFile);
  }

  /// <summary>Solid-архив PPMd: все файлы в одном PPMd-потоке (статистика копится → плотнее на тексте).</summary>
  public static SevenZipArchiveWriteResult BuildPpmdSolidArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
      => BuildSolidArchiveToStream(entries, output, EncodePpmd, PpmdCoderBytes(), "PPMd", progress, token, currentFile);

  /// <summary>Solid-архив Copy: все файлы в одном несжатом потоке (для проверки формата под-потоков).</summary>
  public static SevenZipArchiveWriteResult BuildCopySolidArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
      => BuildSolidArchiveToStream(entries, output, data => data, [0x01, 0x00], "Copy", progress, token, currentFile);

  /// <summary>
  /// Строит solid-архив: ВСЕ файлы с данными склеиваются и сжимаются одним <paramref name="encodeSolid"/>
  /// в один folder с под-потоками на каждый файл. <paramref name="coderBytes"/> — coder для next-header.
  /// </summary>
  internal static SevenZipArchiveWriteResult BuildSolidArchiveToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      Func<byte[], byte[]> encodeSolid,
      byte[] coderBytes,
      string codec,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      IProgress<SevenZipCompressionFileProgress>? currentFile = null)
  {
    ArgumentNullException.ThrowIfNull(entries);
    ArgumentNullException.ThrowIfNull(output);

    if (!output.CanWrite || !output.CanSeek)
      return SevenZipArchiveWriteResult.NotSupported;

    SevenZipArchiveWriteResult validation = ValidateStreamingEntries(entries);
    if (validation != SevenZipArchiveWriteResult.Ok)
      return validation;

    var dataOrder = new List<int>();
    long total = 0;
    for (int i = 0; i < entries.Count; i++)
      if (IsStreamingDataEntry(entries[i]))
      {
        if (entries[i].Length > int.MaxValue)
          return SevenZipArchiveWriteResult.NotSupported;
        dataOrder.Add(i);
        total += entries[i].Length;
      }

    // Вся группа должна поместиться в память (одноразовый Encode). > 2 ГиБ суммарно — пока не поддержано.
    if (total > int.MaxValue)
      return SevenZipArchiveWriteResult.NotSupported;

    long startPos = output.Position;
    output.Write(new byte[SevenZipSignatureHeader.Size]);

    int n = dataOrder.Count;

    // Нет файлов с данными — solid-folder не нужен (архив только из пустых/директорий).
    if (n == 0)
      return FinalizeStreamingArchiveSolid(entries, output, startPos, [], [], [], [], [], []);

    var fileSizes = new long[n];
    var fileCrcs = new uint[n];
    byte[] concat = new byte[(int)total];
    int pos = 0;
    long processed = 0;

    progress?.Report(new SevenZipProgress(0, total));

    for (int k = 0; k < n; k++)
    {
      token.ThrowIfCancellationRequested();

      SevenZipStreamingEntry e = entries[dataOrder[k]];
      byte[] data = ReadExactlyToArray(e.OpenRead(), (int)e.Length);

      fileSizes[k] = data.Length;
      fileCrcs[k] = Crc32.Compute(data);

      Array.Copy(data, 0, concat, pos, data.Length);
      pos += data.Length;

      currentFile?.Report(new SevenZipCompressionFileProgress(e.Name, codec));
      processed += data.Length;
      progress?.Report(new SevenZipProgress(processed, total));
    }

    byte[] packed = encodeSolid(concat);
    output.Write(packed, 0, packed.Length);

    byte[] folderBody = WrapSingleCoderFolderBody(coderBytes);

    return FinalizeStreamingArchiveSolid(
        entries, output, startPos,
        [folderBody],
        [(ulong)packed.Length],
        [[(ulong)total]],
        [n],
        [fileSizes],
        [fileCrcs]);
  }

  // Финализация solid-архива: folders БЕЗ folder-CRC + SubStreamsInfo (per-file размеры/CRC) + FilesInfo.
  private static SevenZipArchiveWriteResult FinalizeStreamingArchiveSolid(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      Stream output,
      long startPos,
      byte[][] folderBodies,
      ulong[] packSizes,
      ulong[][] coderUnpackSizes,
      int[] numUnpackStreamsPerFolder,
      long[][] fileSizesPerFolder,
      uint[][] fileCrcsPerFolder)
  {
    var synthetic = new SevenZipArchiveWriterEntry[entries.Count];
    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipStreamingEntry e = entries[i];
      byte[] marker = IsStreamingDataEntry(e) ? [0] : [];
      synthetic[i] = new SevenZipArchiveWriterEntry(e.Name, marker, e.IsDirectory, e.WindowsAttributes, e.LastWriteTimeUtc);
    }

    if (!TryBuildSolidNextHeader(synthetic, packSizes, coderUnpackSizes, folderBodies,
            numUnpackStreamsPerFolder, fileSizesPerFolder, fileCrcsPerFolder, out byte[] nextHeaderBytes))
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

  private static bool TryBuildSolidNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> syntheticEntries,
      ulong[] packSizes,
      ulong[][] coderUnpackSizes,
      byte[][] folderBodies,
      int[] numUnpackStreamsPerFolder,
      long[][] fileSizesPerFolder,
      uint[][] fileCrcsPerFolder,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
    };

    // Нет folder-ов (только пустые/директории) — MainStreamsInfo не пишем.
    if (folderBodies.Length != 0)
    {
      header.Add(SevenZipNid.MainStreamsInfo);

      if (!TryWriteStreamingPackInfo(header, packSizes))
        return false;

      if (!TryWriteStreamingFoldersUnpackInfoNoCrc(header, folderBodies, coderUnpackSizes))
        return false;

      if (!TryWriteSubStreamsInfo(header, numUnpackStreamsPerFolder, fileSizesPerFolder, fileCrcsPerFolder))
        return false;

      header.Add(SevenZipNid.End);
    }

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

  // UnpackInfo как TryWriteStreamingFoldersUnpackInfo, но БЕЗ секции folder-CRC (per-file CRC уходят
  // в SubStreamsInfo). Иначе CRC дублировался бы и reader вернул бы InvalidData.
  private static bool TryWriteStreamingFoldersUnpackInfoNoCrc(
      List<byte> header, byte[][] folderBodies, ulong[][] coderUnpackSizes)
  {
    if (folderBodies.Length != coderUnpackSizes.Length)
      return false;

    header.Add(SevenZipNid.UnpackInfo);
    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)folderBodies.Length))
      return false;

    header.Add(0x00); // external = 0 (folder-ы встроены)

    for (int i = 0; i < folderBodies.Length; i++)
      header.AddRange(folderBodies[i]);

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < coderUnpackSizes.Length; i++)
      for (int j = 0; j < coderUnpackSizes[i].Length; j++)
        if (!TryWriteUInt64(header, coderUnpackSizes[i][j]))
          return false;

    header.Add(SevenZipNid.End);
    return true;
  }

  // SubStreamsInfo для solid-folder-ов (folder-CRC опущен → ВСЕ per-file CRC пишутся здесь).
  private static bool TryWriteSubStreamsInfo(
      List<byte> header,
      int[] numUnpackStreamsPerFolder,
      long[][] fileSizesPerFolder,
      uint[][] fileCrcsPerFolder)
  {
    header.Add(SevenZipNid.SubStreamsInfo);

    // NumUnpackStream: число под-потоков на folder.
    header.Add(SevenZipNid.NumUnpackStream);
    foreach (int nStreams in numUnpackStreamsPerFolder)
      if (!TryWriteUInt64(header, (ulong)nStreams))
        return false;

    // Size: для folder-а с n>1 пишем (n-1) размеров файлов (последний восстанавливается по folder total).
    header.Add(SevenZipNid.Size);
    for (int f = 0; f < numUnpackStreamsPerFolder.Length; f++)
    {
      int nStreams = numUnpackStreamsPerFolder[f];
      for (int i = 0; i < nStreams - 1; i++)
        if (!TryWriteUInt64(header, (ulong)fileSizesPerFolder[f][i]))
          return false;
    }

    // CRC: folder-CRC нет → все под-потоки «с неизвестным CRC». AllAreDefined=1 + UINT32 на файл.
    header.Add(SevenZipNid.Crc);
    header.Add(0x01);
    for (int f = 0; f < numUnpackStreamsPerFolder.Length; f++)
      foreach (uint crc in fileCrcsPerFolder[f])
        WriteUInt32LittleEndian(header, crc);

    header.Add(SevenZipNid.End);
    return true;
  }
}
