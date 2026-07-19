using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;

namespace Lzma.Core.Lzma2;

// СПАЙК потокового LZMA2-энкодера (ядро Стадии 3): доказывает, что вход можно подавать блоками
// из Stream через КОЛЬЦЕВОЙ буфер, НЕ держа весь файл в памяти, с БАЙТ-В-БАЙТ идентичным выходом
// относительно Encode(ReadOnlySpan) — при известной длине (файл на диске). Позиции — long (>2 ГБ).
//
// Ключ к идентичности: тот же windowMask для prev-буфера (та же хеш-цепочка) + wrap-aware чтение
// байт из кольца вместо input.Slice. Кольцо ≥ windowSize + maxMatch (+запас) → нужные байты
// (история словаря + lookahead) не вытесняются раньше времени.
public static partial class Lzma2LzmaEncoder
{
  private const int StreamHashBytes = 3;
  private const int StreamHashBits = 16;
  private const int StreamHashTableSize = 1 << StreamHashBits;
  private const int StreamMaxChainLength = 128;

  /// <summary>
  /// Потоковое сжатие в память (для тестов/малых входов): читает <paramref name="input"/> блоками
  /// в кольцевой буфер и возвращает тот же LZMA2-поток, что
  /// <see cref="Encode(ReadOnlySpan{byte}, LzmaProperties, int, int, System.Threading.CancellationToken)"/>.
  /// </summary>
  public static byte[] EncodeStreaming(
    Stream input,
    long totalLength,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize = 65536,
    System.Threading.CancellationToken token = default)
  {
    using var ms = new MemoryStream((int)Math.Min(totalLength / 2 + 64, int.MaxValue));
    EncodeStreaming(input, totalLength, lzmaProperties, dictionarySize, ms, maxUnpackChunkSize, token);
    return ms.ToArray();
  }

  /// <summary>
  /// Потоковое сжатие в <paramref name="output"/>: пишет LZMA2-поток по мере кодирования, НЕ держа
  /// весь вход/выход в памяти. Возвращает число записанных байт (long). <paramref name="totalLength"/>
  /// — заранее известный размер входа (файл на диске). Выход идентичен <see cref="Encode"/>.
  /// </summary>
  // Как часто (по числу обработанных байт входа) репортить прогресс внутри файла.
  private const long StreamProgressIntervalBytes = 1 << 21; // 2 МиБ

  public static long EncodeStreaming(
    Stream input,
    long totalLength,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    Stream output,
    int maxUnpackChunkSize = 65536,
    System.Threading.CancellationToken token = default,
    IProgress<long>? bytesProgress = null)
  {
    ArgumentNullException.ThrowIfNull(input);
    ArgumentNullException.ThrowIfNull(output);
    ArgumentOutOfRangeException.ThrowIfNegative(totalLength);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    if (maxUnpackChunkSize > 65536)
      throw new ArgumentOutOfRangeException(nameof(maxUnpackChunkSize),
        "Размер чанка ограничен 64 КБ: COPY-чанк и packSize LZMA-чанка хранят размер в 16 битах.");

    byte propsByte = lzmaProperties.ToByteOrThrow();

    var ms = new CountingWriteStream(output);

    if (totalLength == 0)
    {
      ms.WriteByte(0x00);
      return ms.BytesWritten;
    }

    int maxMatch = LzmaConstants.MatchMaxLen;

    // windowSize/windowMask — как в Encode (иначе разойдётся хеш-цепочка → другой выход).
    int cappedForWindow = (int)Math.Min(dictionarySize, totalLength);
    int windowSize = WindowSizePow2(cappedForWindow);
    int windowMask = windowSize - 1;

    // Кольцо входа: строго больше windowSize + maxMatch, чтобы «история + lookahead» не вытеснялись.
    int ringSize = NextPow2Streaming(windowSize + maxMatch + 1);
    int ringMask = ringSize - 1;
    byte[] ring = new byte[ringSize];

    long[] head = new long[StreamHashTableSize];
    Array.Fill(head, -1L);
    long[] prev = new long[windowSize];

    long filled = 0; // абсолютная позиция следующего непрочитанного байта

    void EnsureFilled(long upto)
    {
      long target = Math.Min(upto, totalLength);
      while (filled < target)
      {
        int idx = (int)(filled & ringMask);
        int want = (int)Math.Min(ringSize - idx, target - filled);
        int got = input.Read(ring, idx, want);
        if (got <= 0)
          throw new EndOfStreamException("Входной поток короче заявленной длины.");
        filled += got;
      }
    }

    var encoder = new LzmaEncoder(lzmaProperties, dictionarySize);
    var sink = new ChunkingSink(ms, encoder, propsByte, maxUnpackChunkSize, token);

    Span<LzmaMatch> matches = stackalloc LzmaMatch[256];

    long i = 0;
    long n = totalLength;
    long nextReport = StreamProgressIntervalBytes;

    while (i < n)
    {
      EnsureFilled(i + maxMatch);

      int count = FindMatchesStreaming(ring, ringMask, head, prev, windowMask, i, n, maxMatch, dictionarySize, matches);

      int normalLen = count > 0 ? matches[count - 1].Length : 0;
      int normalDist = count > 0 ? matches[count - 1].Distance : 0;

      int repLen = 0;
      int repDist = 0;
      for (int r = 0; r < 4; r++)
      {
        int d = encoder.CurrentRepDistance(r);
        if (d > i)
          continue;

        int len = RepMatchLengthStreaming(ring, ringMask, i, d, n, maxMatch);
        if (len > repLen)
        {
          repLen = len;
          repDist = d;
        }
      }

      LzmaEncodeOp op;
      int advance;

      if (repLen >= MinMatch && repLen + 1 >= normalLen)
      {
        op = LzmaEncodeOp.Match(repDist, repLen);
        advance = repLen;
      }
      else if (normalLen >= MinMatch)
      {
        op = LzmaEncodeOp.Match(normalDist, normalLen);
        advance = normalLen;
      }
      else
      {
        op = LzmaEncodeOp.Lit(ring[i & ringMask]);
        advance = 1;
      }

      sink.Emit(op);

      long end = i + advance;
      while (i < end)
      {
        EnsureFilled(i + maxMatch);
        InsertStreaming(ring, ringMask, head, prev, windowMask, i, n);
        i++;
      }

      // Прогресс внутри файла (по числу обработанных байт входа) — чтобы бар двигался на больших файлах.
      if (bytesProgress is not null && i >= nextReport)
      {
        bytesProgress.Report(i);
        nextReport = i + StreamProgressIntervalBytes;
      }
    }

    sink.Finish();

    ms.WriteByte(0x00);
    return ms.BytesWritten;
  }

  // Write-only обёртка, считающая записанные байты (для packSize потокового энкодера).
  private sealed class CountingWriteStream(Stream inner) : Stream
  {
    public long BytesWritten { get; private set; }

    public override bool CanWrite => true;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
      inner.Write(buffer);
      BytesWritten += buffer.Length;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
      inner.Write(buffer, offset, count);
      BytesWritten += count;
    }

    public override void WriteByte(byte value)
    {
      inner.WriteByte(value);
      BytesWritten++;
    }

    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }

  /// <summary>
  /// МНОГОПОТОЧНОЕ блочное сжатие в <paramref name="output"/>: вход режется на независимые блоки,
  /// блоки волны сжимаются ПАРАЛЛЕЛЬНО (каждый — самостоятельный LZMA2 со сбросом словаря), затем
  /// пишутся по порядку. Как в 7-Zip (mt): кратное ускорение ценой небольшого проигрыша сжатия
  /// (матчи не переходят границу блока). Возвращает packSize; CRC несжатого — через
  /// <paramref name="contentCrc"/>. Пиковая память ≈ степень_параллелизма × blockSize.
  /// </summary>
  public static long EncodeParallelToStream(
    Stream input,
    long totalLength,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    Stream output,
    out uint contentCrc,
    int blockSize = 0,
    int maxDegreeOfParallelism = 0,
    IProgress<long>? bytesProgress = null,
    System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(input);
    ArgumentNullException.ThrowIfNull(output);
    ArgumentOutOfRangeException.ThrowIfNegative(totalLength);

    // Блок по умолчанию >= словаря (иначе словарь внутри блока обрезается) и не меньше 1 МиБ.
    if (blockSize <= 0)
      blockSize = Math.Max(dictionarySize, 1 << 20);

    if (maxDegreeOfParallelism <= 0)
      maxDegreeOfParallelism = Environment.ProcessorCount;

    var counting = new CountingWriteStream(output);
    uint crc = Crc32.InitialState;
    long produced = 0;

    if (totalLength == 0)
    {
      counting.WriteByte(0x00);
      contentCrc = Crc32.Finalize(crc);
      return counting.BytesWritten;
    }

    var options = new ParallelOptions
    {
      MaxDegreeOfParallelism = maxDegreeOfParallelism,
      CancellationToken = token,
    };

    while (produced < totalLength)
    {
      token.ThrowIfCancellationRequested();

      // Читаем волну блоков ПОСЛЕДОВАТЕЛЬНО (+CRC по ходу, в порядке файла).
      var blocks = new List<byte[]>(maxDegreeOfParallelism);
      for (int p = 0; p < maxDegreeOfParallelism && produced < totalLength; p++)
      {
        int len = (int)Math.Min(blockSize, totalLength - produced);
        byte[] buffer = new byte[len];
        ReadFully(input, buffer, len);
        crc = Crc32.Update(crc, buffer);
        blocks.Add(buffer);
        produced += len;
      }

      // Сжимаем блоки волны ПАРАЛЛЕЛЬНО (каждый независим).
      var compressed = new byte[blocks.Count][];
      Parallel.For(0, blocks.Count, options, k =>
      {
        byte[] full = Encode(blocks[k], lzmaProperties, dictionarySize, 65536, token);
        compressed[k] = full[..^1]; // без завершающего end-marker (0x00)
      });

      // Пишем сжатые блоки СТРОГО по порядку.
      for (int k = 0; k < compressed.Length; k++)
        counting.Write(compressed[k], 0, compressed[k].Length);

      bytesProgress?.Report(produced);
    }

    counting.WriteByte(0x00); // единственный end-marker в конце потока
    contentCrc = Crc32.Finalize(crc);
    return counting.BytesWritten;
  }

  private static void ReadFully(Stream input, byte[] buffer, int count)
  {
    int offset = 0;
    while (offset < count)
    {
      int got = input.Read(buffer, offset, count - offset);
      if (got <= 0)
        throw new EndOfStreamException("Входной поток короче заявленной длины.");
      offset += got;
    }
  }

  // Ring-aware поиск совпадений: точная копия LzmaMatchFinder.FindMatchesCyclic, но байты читаются
  // из кольца (wrap-aware), позиции — long, лимит длины — по totalLength.
  private static int FindMatchesStreaming(
      byte[] ring, int ringMask, long[] head, long[] prev, int windowMask,
      long pos, long totalLength, int maxMatch, int maxDistance, Span<LzmaMatch> matches)
  {
    if (pos + StreamHashBytes > totalLength)
      return 0;

    int count = 0;
    int bestLength = 0;

    long candidate = head[HashStreaming(ring, ringMask, pos)];
    int chain = StreamMaxChainLength;

    while (candidate >= 0 && chain-- > 0)
    {
      long distance = pos - candidate;
      if (distance > maxDistance)
        break;

      // Быстрый отказ (как в FindMatchesCyclic, идентично для байт-в-байт): кандидат, не совпавший по
      // байту на позиции bestLength, не улучшит длину — пропускаем дорогой MatchLength.
      if (bestLength > 0)
      {
        if (pos + bestLength >= totalLength)
          break;

        if (ring[(candidate + bestLength) & ringMask] != ring[(pos + bestLength) & ringMask])
        {
          candidate = prev[candidate & windowMask];
          continue;
        }
      }

      int length = MatchLengthStreaming(ring, ringMask, candidate, pos, totalLength, maxMatch);

      if (length > bestLength)
      {
        bestLength = length;
        matches[count++] = new LzmaMatch(length, (int)distance);

        if (length >= maxMatch)
          break;
      }

      candidate = prev[candidate & windowMask];
    }

    return count;
  }

  private static void InsertStreaming(
      byte[] ring, int ringMask, long[] head, long[] prev, int windowMask, long pos, long totalLength)
  {
    if (pos + StreamHashBytes > totalLength)
      return;

    int h = HashStreaming(ring, ringMask, pos);
    prev[pos & windowMask] = head[h];
    head[h] = pos;
  }

  // Длина совпадения в кольце. Быстрый путь: если оба среза не пересекают границу кольца —
  // векторное CommonPrefixLength (как в Encode); иначе побайтный wrap-aware проход. Результат
  // идентичен побайтному (те же байты сравниваются).
  private static int MatchLengthStreaming(
      byte[] ring, int ringMask, long source, long current, long totalLength, int maxMatch)
  {
    int limit = (int)Math.Min(maxMatch, totalLength - current);
    if (limit <= 0)
      return 0;

    return RingCommonPrefix(ring, ringMask, source, current, limit);
  }

  // Длина rep-совпадения: как RepMatchLength, но из кольца.
  private static int RepMatchLengthStreaming(
      byte[] ring, int ringMask, long pos, int distance, long totalLength, int maxMatch)
  {
    int limit = (int)Math.Min(maxMatch, totalLength - pos);
    if (limit <= 0)
      return 0;

    return RingCommonPrefix(ring, ringMask, pos - distance, pos, limit);
  }

  // Общая длина совпадения байт кольца в позициях source и current на длину до limit.
  private static int RingCommonPrefix(byte[] ring, int ringMask, long source, long current, int limit)
  {
    int sIdx = (int)(source & ringMask);
    int cIdx = (int)(current & ringMask);

    // Быстрый путь: ни один срез не переходит через конец кольца → векторное сравнение.
    if (sIdx + limit <= ring.Length && cIdx + limit <= ring.Length)
      return ring.AsSpan(sIdx, limit).CommonPrefixLength(ring.AsSpan(cIdx, limit));

    int k = 0;
    while (k < limit && ring[(source + k) & ringMask] == ring[(current + k) & ringMask])
      k++;

    return k;
  }

  private static int HashStreaming(byte[] ring, int ringMask, long pos)
  {
    uint value =
        ((uint)ring[pos & ringMask] << 16)
        | ((uint)ring[(pos + 1) & ringMask] << 8)
        | ring[(pos + 2) & ringMask];

    return (int)((value * 2654435761u) >> (32 - StreamHashBits));
  }

  private static int NextPow2Streaming(int n)
  {
    if (n < 1)
      n = 1;

    int size = 1;
    while (size < n)
      size <<= 1;

    return size;
  }
}
