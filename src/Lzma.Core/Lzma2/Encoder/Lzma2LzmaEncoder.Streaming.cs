using System.IO;

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
  /// Потоковое сжатие: читает <paramref name="input"/> блоками в кольцевой буфер и выдаёт тот же
  /// LZMA2-поток, что <see cref="Encode(ReadOnlySpan{byte}, LzmaProperties, int, int, System.Threading.CancellationToken)"/>.
  /// <paramref name="totalLength"/> — заранее известный размер входа (файл на диске).
  /// </summary>
  public static byte[] EncodeStreaming(
    Stream input,
    long totalLength,
    LzmaProperties lzmaProperties,
    int dictionarySize,
    int maxUnpackChunkSize = 65536,
    System.Threading.CancellationToken token = default)
  {
    ArgumentNullException.ThrowIfNull(input);
    ArgumentOutOfRangeException.ThrowIfNegative(totalLength);
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxUnpackChunkSize);

    if (maxUnpackChunkSize > 65536)
      throw new ArgumentOutOfRangeException(nameof(maxUnpackChunkSize),
        "Размер чанка ограничен 64 КБ: COPY-чанк и packSize LZMA-чанка хранят размер в 16 битах.");

    byte propsByte = lzmaProperties.ToByteOrThrow();

    using var ms = new MemoryStream((int)Math.Min(totalLength / 2 + 64, int.MaxValue));

    if (totalLength == 0)
    {
      ms.WriteByte(0x00);
      return ms.ToArray();
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
    }

    sink.Finish();

    ms.WriteByte(0x00);
    return ms.ToArray();
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

  // Длина совпадения в кольце (wrap-aware, побайтно — для спайка важна корректность, не скорость).
  private static int MatchLengthStreaming(
      byte[] ring, int ringMask, long source, long current, long totalLength, int maxMatch)
  {
    int limit = (int)Math.Min(maxMatch, totalLength - current);
    int k = 0;
    while (k < limit && ring[(source + k) & ringMask] == ring[(current + k) & ringMask])
      k++;

    return k;
  }

  // Длина rep-совпадения: как RepMatchLength, но из кольца.
  private static int RepMatchLengthStreaming(
      byte[] ring, int ringMask, long pos, int distance, long totalLength, int maxMatch)
  {
    int limit = (int)Math.Min(maxMatch, totalLength - pos);
    if (limit <= 0)
      return 0;

    long source = pos - distance;
    int k = 0;
    while (k < limit && ring[(source + k) & ringMask] == ring[(pos + k) & ringMask])
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
