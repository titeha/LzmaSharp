namespace Lzma.Core.BZip2;

/// <summary>
/// <para>Управляемый энкодер BZip2, без unsafe.</para>
/// <para>
/// Реализует прямой конвейер bzip2: RLE1 → BWT (прямое преобразование) → MTF + RLE2
/// (RUNA/RUNB) → Huffman-кодирование с групповыми таблицами. На этом шаге используется
/// одна общая Huffman-таблица, продублированная в 2 группы (валидный поток; адаптивные
/// мультигрупповые таблицы — возможная последующая оптимизация ради лучшего сжатия).
/// </para>
/// </summary>
public static class BZip2Encoder
{
  private const int BlockSize100k = 1;                  // размер блока = 100 КБ (level '1')
  private const int BlockSize = BlockSize100k * 100000;
  private const int MaxInputPerBlock = BlockSize * 4 / 5; // запас под возможное расширение RLE1

  private const long BlockMagic = 0x314159265359;
  private const long EndOfStreamMagic = 0x177245385090;

  private const int RunA = 0;
  private const int RunB = 1;
  private const int MaxHuffLen = 20;

  private static readonly uint[] CrcTable = BuildCrcTable();

  /// <summary>
  /// Кодирует <paramref name="input"/> в полный bzip2-поток.
  /// </summary>
  public static byte[] Encode(ReadOnlySpan<byte> input)
  {
    var writer = new MsbBitWriter(input.Length / 2 + 64);

    // Заголовок потока: 'B' 'Z' 'h' '1'.
    writer.WriteBits('B', 8);
    writer.WriteBits('Z', 8);
    writer.WriteBits('h', 8);
    writer.WriteBits((uint)('0' + BlockSize100k), 8);

    uint combinedCrc = 0;
    int offset = 0;

    // Крупные буферы (BWT и MTF) переиспользуются между блоками: иначе каждый блок
    // выделял бы несколько массивов >85 КБ в LOH, вызывая постоянные Gen2-сборки —
    // именно GC-давление, а не CPU, доминировало во времени encode.
    var scratch = new BlockScratch();

    while (offset < input.Length)
    {
      int take = Math.Min(MaxInputPerBlock, input.Length - offset);
      ReadOnlySpan<byte> chunk = input.Slice(offset, take);
      offset += take;

      uint blockCrc = ComputeCrc(chunk);
      combinedCrc = ((combinedCrc << 1) | (combinedCrc >> 31)) ^ blockCrc;

      EncodeBlock(writer, chunk, blockCrc, scratch);
    }

    writer.WriteBits((uint)(EndOfStreamMagic >> 24), 24);
    writer.WriteBits((uint)(EndOfStreamMagic & 0xFFFFFF), 24);
    writer.WriteBits(combinedCrc, 32);
    writer.Flush();

    return writer.ToArray();
  }

  /// <summary>Переиспользуемые между блоками крупные буферы (во избежание LOH-мусора).</summary>
  private sealed class BlockScratch
  {
    public readonly int[] Sa = new int[BlockSize];
    public readonly int[] Rank = new int[BlockSize];
    public readonly int[] Tmp = new int[BlockSize];
    public readonly long[] Key = new long[BlockSize];
    public readonly byte[] Bwt = new byte[BlockSize];
    public readonly List<int> Mtf = new(BlockSize + 1);

    // Буферы радикс-сортировки рангов (counting sort).
    public readonly int[] Cnt = new int[BlockSize + 1];
    public readonly int[] SaTmp = new int[BlockSize];
  }

  private static void EncodeBlock(MsbBitWriter writer, ReadOnlySpan<byte> chunk, uint blockCrc, BlockScratch scratch)
  {
    // 1) RLE1.
    byte[] rle = Rle1Encode(chunk);
    int n = rle.Length;

    // 2) BWT (результат в scratch.Bwt[0..n)).
    BurrowsWheeler(rle, scratch, out int origPtr);
    byte[] bwt = scratch.Bwt;

    // 3) Карта используемых символов.
    bool[] inUse = new bool[256];
    for (int i = 0; i < n; i++)
      inUse[bwt[i]] = true;

    byte[] seqToUnseq = new byte[256];
    int nInUse = 0;
    for (int i = 0; i < 256; i++)
      if (inUse[i])
        seqToUnseq[nInUse++] = (byte)i;

    int eob = nInUse + 1;
    int alphaSize = nInUse + 2;

    // 4) MTF + RLE2.
    List<int> mtfSymbols = scratch.Mtf;
    int[] freq = new int[alphaSize];
    MtfAndRle2(bwt, n, seqToUnseq, nInUse, mtfSymbols, freq, eob);

    // 5) Huffman (одна таблица, продублированная в 2 группы).
    // Частоты с полом 1, чтобы все символы алфавита получили длину 1..20.
    int[] freqFloored = new int[alphaSize];
    for (int i = 0; i < alphaSize; i++)
      freqFloored[i] = Math.Max(1, freq[i]);

    int[] lengths = BuildHuffmanLengths(freqFloored, MaxHuffLen);
    int[] codes = BuildCanonicalCodes(lengths);

    // 6) Запись блока.
    writer.WriteBits((uint)(BlockMagic >> 24), 24);
    writer.WriteBits((uint)(BlockMagic & 0xFFFFFF), 24);
    writer.WriteBits(blockCrc, 32);
    writer.WriteBits(0, 1);                  // randomized = 0
    writer.WriteBits((uint)origPtr, 24);

    WriteSymbolMap(writer, inUse);

    const int numGroups = 2;
    writer.WriteBits(numGroups, 3);

    int numSelectors = (mtfSymbols.Count + 49) / 50;
    writer.WriteBits((uint)numSelectors, 15);

    // Селекторы: все группа 0 => MTF-значение 0 => унарный код "0" (один нулевой бит).
    for (int i = 0; i < numSelectors; i++)
      writer.WriteBits(0, 1);

    // Таблицы длин для каждой группы (обе одинаковые).
    for (int g = 0; g < numGroups; g++)
      WriteCodeLengths(writer, lengths);

    // Данные: каждый MTF-символ кодируется таблицей группы 0.
    foreach (int sym in mtfSymbols)
      writer.WriteBits((uint)codes[sym], lengths[sym]);
  }

  // ============================================================
  // RLE1 (первая стадия): прогон 4+ одинаковых байт => 4 байта + счётчик (0..251).
  // ============================================================

  private static byte[] Rle1Encode(ReadOnlySpan<byte> data)
  {
    var output = new List<byte>(data.Length + data.Length / 4 + 4);

    int i = 0;
    while (i < data.Length)
    {
      byte b = data[i];
      int run = 1;
      while (i + run < data.Length && data[i + run] == b && run < 255 + 4)
        run++;

      if (run < 4)
      {
        for (int k = 0; k < run; k++)
          output.Add(b);
      }
      else
      {
        output.Add(b);
        output.Add(b);
        output.Add(b);
        output.Add(b);
        output.Add((byte)(run - 4));
      }

      i += run;
    }

    return [.. output];
  }

  // ============================================================
  // BWT (прямое) через prefix-doubling сортировку циклических ротаций.
  // ============================================================

  private static void BurrowsWheeler(byte[] t, BlockScratch scratch, out int origPtr)
  {
    int n = t.Length;
    byte[] bwt = scratch.Bwt;
    origPtr = 0;

    if (n == 0)
      return;

    if (n == 1)
    {
      bwt[0] = t[0];
      origPtr = 0;
      return;
    }

    int[] sa = scratch.Sa;
    int[] rank = scratch.Rank;
    int[] tmp = scratch.Tmp;
    long[] key = scratch.Key;
    int[] cnt = scratch.Cnt;
    int[] saTmp = scratch.SaTmp;

    // Каждый раунд сортируем позиции по паре рангов (rank[i], rank[i+gap]) РАДИКС-сортировкой
    // (две стабильные counting-сортировки по 32-битным половинам), O(n) на раунд вместо
    // O(n·log n) у сравнительной сортировки. Ранги в [0,n), поэтому counting sort применима
    // напрямую. Ключ key[i] = (rank[i] << 32) | rank[i+gap]; сортировка по возрастанию key
    // = лексикографический порядок пар. Все буферы переиспользуются между блоками.
    for (int i = 0; i < n; i++)
      rank[i] = t[i];

    // Диапазон значений рангов для counting sort: в первом раунде ранги = байты [0,255],
    // в последующих — плотные [0,n). Берём общий верхний предел.
    int rmax = Math.Max(256, n);

    for (int gap = 1; ; gap *= 2)
    {
      for (int i = 0; i < n; i++)
      {
        int j = i + gap;
        if (j >= n)
          j -= n;
        if (j >= n)        // gap может превысить n на последнем раунде
          j %= n;

        key[i] = ((long)rank[i] << 32) | (uint)rank[j];
      }

      // Проход 1 (младшая половина = rank[i+gap]): стабильная counting sort позиций → saTmp.
      Array.Clear(cnt, 0, rmax + 1);
      for (int i = 0; i < n; i++)
        cnt[(int)(key[i] & 0xFFFFFFFF) + 1]++;
      for (int v = 1; v <= rmax; v++)
        cnt[v] += cnt[v - 1];
      for (int i = 0; i < n; i++)
        saTmp[cnt[(int)(key[i] & 0xFFFFFFFF)]++] = i;

      // Проход 2 (старшая половина = rank[i]): стабильная counting sort saTmp → sa.
      Array.Clear(cnt, 0, rmax + 1);
      for (int i = 0; i < n; i++)
        cnt[(int)(key[i] >> 32) + 1]++;
      for (int v = 1; v <= rmax; v++)
        cnt[v] += cnt[v - 1];
      for (int k = 0; k < n; k++)
      {
        int i = saTmp[k];
        sa[cnt[(int)(key[i] >> 32)]++] = i;
      }

      tmp[sa[0]] = 0;
      for (int i = 1; i < n; i++)
        tmp[sa[i]] = tmp[sa[i - 1]] + (key[sa[i]] != key[sa[i - 1]] ? 1 : 0);

      Array.Copy(tmp, rank, n);

      if (rank[sa[n - 1]] == n - 1)
        break;

      if (gap >= n)
        break;
    }

    for (int i = 0; i < n; i++)
    {
      int s = sa[i];
      bwt[i] = t[(s + n - 1) % n];
      if (s == 0)
        origPtr = i;
    }
  }

  // ============================================================
  // MTF + RLE2.
  // ============================================================

  private static void MtfAndRle2(byte[] bwt, int n, byte[] seqToUnseq, int nInUse, List<int> output, int[] freq, int eob)
  {
    output.Clear(); // список переиспользуется между блоками

    byte[] mtf = new byte[nInUse];
    for (int i = 0; i < nInUse; i++)
      mtf[i] = seqToUnseq[i];

    int zeroRun = 0;

    for (int p = 0; p < n; p++)
    {
      byte b = bwt[p];
      // Индекс b в MTF-списке.
      int idx = 0;
      while (mtf[idx] != b)
        idx++;

      // Move-to-front.
      if (idx != 0)
      {
        byte tmp = mtf[idx];
        for (int k = idx; k > 0; k--)
          mtf[k] = mtf[k - 1];
        mtf[0] = tmp;
      }

      if (idx == 0)
      {
        zeroRun++;
        continue;
      }

      FlushZeroRun(ref zeroRun, output, freq);

      int sym = idx + 1;
      output.Add(sym);
      freq[sym]++;
    }

    FlushZeroRun(ref zeroRun, output, freq);

    output.Add(eob);
    freq[eob]++;
  }

  private static void FlushZeroRun(ref int zeroRun, List<int> output, int[] freq)
  {
    if (zeroRun == 0)
      return;

    // Биективная база 2: цифры 1 (RUNA) и 2 (RUNB).
    int r = zeroRun;
    while (r > 0)
    {
      r--;
      int sym = (r & 1) == 0 ? RunA : RunB;
      output.Add(sym);
      freq[sym]++;
      r >>= 1;
    }

    zeroRun = 0;
  }

  // ============================================================
  // Запись карты символов и таблиц длин.
  // ============================================================

  private static void WriteSymbolMap(MsbBitWriter writer, bool[] inUse)
  {
    int used16 = 0;
    for (int i = 0; i < 16; i++)
      for (int j = 0; j < 16; j++)
        if (inUse[i * 16 + j])
        {
          used16 |= 0x8000 >> i;
          break;
        }

    writer.WriteBits((uint)used16, 16);

    for (int i = 0; i < 16; i++)
    {
      if ((used16 & (0x8000 >> i)) == 0)
        continue;

      int bits = 0;
      for (int j = 0; j < 16; j++)
        if (inUse[i * 16 + j])
          bits |= 0x8000 >> j;

      writer.WriteBits((uint)bits, 16);
    }
  }

  private static void WriteCodeLengths(MsbBitWriter writer, int[] lengths)
  {
    int current = lengths[0];
    writer.WriteBits((uint)current, 5);

    for (int s = 0; s < lengths.Length; s++)
    {
      int target = lengths[s];
      while (current != target)
      {
        writer.WriteBits(1, 1); // продолжаем
        if (target > current)
        {
          writer.WriteBits(0, 1); // increment
          current++;
        }
        else
        {
          writer.WriteBits(1, 1); // decrement
          current--;
        }
      }

      writer.WriteBits(0, 1); // стоп
    }
  }

  // ============================================================
  // Huffman: length-limited длины + канонические коды.
  // ============================================================

  private static int[] BuildHuffmanLengths(int[] freq, int maxLen)
  {
    int n = freq.Length;
    var lengths = new int[n];

    // Все символы присутствуют (частоты с полом 1), поэтому строим по всем.
    int maxNodes = 2 * n - 1;
    long[] weight = new long[maxNodes];
    int[] parent = new int[maxNodes];
    for (int i = 0; i < n; i++)
      weight[i] = freq[i];

    var pq = new PriorityQueue<int, (long, int)>();
    for (int i = 0; i < n; i++)
      pq.Enqueue(i, (weight[i], i));

    int next = n;
    while (pq.Count > 1)
    {
      int a = pq.Dequeue();
      int b = pq.Dequeue();
      weight[next] = weight[a] + weight[b];
      parent[a] = next;
      parent[b] = next;
      pq.Enqueue(next, (weight[next], next));
      next++;
    }

    int root = next - 1;

    Span<int> blCount = stackalloc int[64];
    int maxDepth = 0;
    int[] symDepth = new int[n];
    for (int i = 0; i < n; i++)
    {
      int depth = 0;
      int node = i;
      while (node != root)
      {
        node = parent[node];
        depth++;
      }

      symDepth[i] = depth;
      blCount[depth]++;
      if (depth > maxDepth)
        maxDepth = depth;
    }

    if (maxDepth > maxLen)
    {
      int overflow = 0;
      for (int b = maxLen + 1; b <= maxDepth; b++)
        overflow += blCount[b];

      for (int b = maxLen + 1; b <= maxDepth; b++)
      {
        blCount[maxLen] += blCount[b];
        blCount[b] = 0;
      }

      while (overflow > 0)
      {
        int b = maxLen - 1;
        while (blCount[b] == 0)
          b--;

        blCount[b]--;
        blCount[b + 1] += 2;
        blCount[maxLen]--;
        overflow -= 2;
      }

      maxDepth = maxLen;
    }

    int[] order = new int[n];
    for (int i = 0; i < n; i++)
      order[i] = i;

    Array.Sort(order, (x, y) =>
    {
      int c = freq[x].CompareTo(freq[y]);
      return c != 0 ? c : x.CompareTo(y);
    });

    int idx = 0;
    for (int b = maxDepth; b >= 1; b--)
    {
      int cnt = blCount[b];
      while (cnt-- > 0)
        lengths[order[idx++]] = b;
    }

    return lengths;
  }

  private static int[] BuildCanonicalCodes(int[] lengths)
  {
    Span<int> blCount = stackalloc int[MaxHuffLen + 1];
    foreach (int l in lengths)
      blCount[l]++;

    Span<int> nextCode = stackalloc int[MaxHuffLen + 1];
    int code = 0;
    blCount[0] = 0;
    for (int bits = 1; bits <= MaxHuffLen; bits++)
    {
      code = (code + blCount[bits - 1]) << 1;
      nextCode[bits] = code;
    }

    var codes = new int[lengths.Length];
    for (int i = 0; i < lengths.Length; i++)
      codes[i] = nextCode[lengths[i]]++;

    return codes;
  }

  // ============================================================
  // bzip2 CRC-32 (big-endian, полином 0x04C11DB7).
  // ============================================================

  private static uint[] BuildCrcTable()
  {
    var table = new uint[256];
    for (uint n = 0; n < 256; n++)
    {
      uint c = n << 24;
      for (int k = 0; k < 8; k++)
        c = (c & 0x80000000) != 0 ? (c << 1) ^ 0x04C11DB7 : c << 1;

      table[n] = c;
    }

    return table;
  }

  private static uint ComputeCrc(ReadOnlySpan<byte> data)
  {
    uint crc = 0xFFFFFFFF;
    foreach (byte b in data)
      crc = (crc << 8) ^ CrcTable[(crc >> 24) ^ b];

    return ~crc;
  }

  // ============================================================
  // MSB-first bit writer (bzip2 пишет биты старшим вперёд).
  // ============================================================

  private sealed class MsbBitWriter
  {
    private readonly List<byte> _bytes;
    private int _bitBuffer;
    private int _bitCount;

    public MsbBitWriter(int capacityHint) => _bytes = new List<byte>(Math.Max(16, capacityHint));

    public void WriteBits(uint value, int count)
    {
      for (int i = count - 1; i >= 0; i--)
      {
        _bitBuffer = (_bitBuffer << 1) | (int)((value >> i) & 1);
        _bitCount++;
        if (_bitCount == 8)
        {
          _bytes.Add((byte)_bitBuffer);
          _bitBuffer = 0;
          _bitCount = 0;
        }
      }
    }

    public void Flush()
    {
      if (_bitCount > 0)
      {
        _bitBuffer <<= 8 - _bitCount;
        _bytes.Add((byte)_bitBuffer);
        _bitBuffer = 0;
        _bitCount = 0;
      }
    }

    public byte[] ToArray() => [.. _bytes];
  }
}
