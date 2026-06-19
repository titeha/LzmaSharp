namespace Lzma.Core.Deflate;

/// <summary>
/// <para>Управляемый энкодер DEFLATE (RFC 1951), без unsafe.</para>
/// <para>
/// Первый шаг: LZ77 (хеш-цепочки, жадный разбор) + блоки с фиксированными таблицами
/// Хаффмана, плюс fallback на stored-блоки для несжимаемых данных. Динамические таблицы
/// Хаффмана — отдельный последующий шаг для лучшего сжатия.
/// </para>
/// </summary>
public static class DeflateEncoder
{
  private const int MinMatch = 3;
  private const int MaxMatch = 258;
  private const int WindowSize = 32768;
  private const int HashBits = 15;
  private const int HashSize = 1 << HashBits;
  private const int MaxChain = 128;

  // База и доп. биты длин (коды 257..285).
  private static readonly int[] LengthBase =
  [
      3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31,
      35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258
  ];

  private static readonly int[] LengthExtra =
  [
      0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2,
      3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0
  ];

  private static readonly int[] DistBase =
  [
      1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193,
      257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145,
      8193, 12289, 16385, 24577
  ];

  private static readonly int[] DistExtra =
  [
      0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6,
      7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13
  ];

  // Таблицы кодирования: длина/дистанция -> (код, доп. биты, значение доп. бит).
  private static readonly (byte Code, byte ExtraBits, ushort ExtraVal)[] LenEncode = BuildLengthEncode();
  private static readonly (byte Code, byte ExtraBits, ushort ExtraVal)[] DistEncode = BuildDistEncode();

  // Фиксированные коды Хаффмана (канонические, MSB-first) + их длины.
  private static readonly (int Code, int Len)[] FixedLitLen = BuildFixedLitLen();
  private static readonly (int Code, int Len)[] FixedDist = BuildFixedDist();

  /// <summary>
  /// Кодирует <paramref name="input"/> в raw DEFLATE-поток.
  /// </summary>
  public static byte[] Encode(ReadOnlySpan<byte> input)
  {
    List<Token> tokens = Lz77(input);

    byte[] fixedBytes = EncodeFixed(tokens, input.Length);

    // Stored-вариант: на каждый блок (<= 65535) — 5 байт заголовка.
    long storedSize = ComputeStoredSize(input.Length);

    if (fixedBytes.Length <= storedSize)
      return fixedBytes;

    return EncodeStored(input);
  }

  private readonly struct Token
  {
    public readonly ushort LitOrLen;
    public readonly ushort Dist; // 0 => литерал; иначе дистанция совпадения

    private Token(ushort litOrLen, ushort dist)
    {
      LitOrLen = litOrLen;
      Dist = dist;
    }

    public static Token Literal(byte b) => new(b, 0);
    public static Token Match(int length, int distance) => new((ushort)length, (ushort)distance);
  }

  // ============================================================
  // LZ77 (жадный разбор хеш-цепочками)
  // ============================================================

  private static List<Token> Lz77(ReadOnlySpan<byte> input)
  {
    var tokens = new List<Token>();
    int n = input.Length;
    if (n == 0)
      return tokens;

    int[] head = new int[HashSize];
    Array.Fill(head, -1);
    int[] prev = new int[n];

    int i = 0;
    while (i < n)
    {
      int bestLen = 0;
      int bestDist = 0;

      if (i + MinMatch <= n)
      {
        int h = Hash(input, i);
        int cand = head[h];
        int chain = MaxChain;

        while (cand >= 0 && chain-- > 0)
        {
          int dist = i - cand;
          if (dist > WindowSize)
            break;

          int len = MatchLength(input, cand, i, n);
          if (len > bestLen)
          {
            bestLen = len;
            bestDist = dist;
            if (len >= MaxMatch)
              break;
          }

          cand = prev[cand];
        }
      }

      if (bestLen >= MinMatch)
      {
        tokens.Add(Token.Match(bestLen, bestDist));
        int end = i + bestLen;
        while (i < end)
        {
          Insert(input, i, head, prev);
          i++;
        }
      }
      else
      {
        tokens.Add(Token.Literal(input[i]));
        Insert(input, i, head, prev);
        i++;
      }
    }

    return tokens;
  }

  private static int MatchLength(ReadOnlySpan<byte> input, int source, int current, int n)
  {
    int max = Math.Min(MaxMatch, n - current);
    int len = 0;
    while (len < max && input[source + len] == input[current + len])
      len++;

    return len;
  }

  private static void Insert(ReadOnlySpan<byte> input, int pos, int[] head, int[] prev)
  {
    if (pos + MinMatch > input.Length)
      return;

    int h = Hash(input, pos);
    prev[pos] = head[h];
    head[h] = pos;
  }

  private static int Hash(ReadOnlySpan<byte> input, int pos)
  {
    uint value = ((uint)input[pos] << 16) | ((uint)input[pos + 1] << 8) | input[pos + 2];
    return (int)((value * 2654435761u) >> (32 - HashBits));
  }

  // ============================================================
  // Кодирование фиксированными таблицами Хаффмана
  // ============================================================

  private static byte[] EncodeFixed(List<Token> tokens, int inputLength)
  {
    var writer = new BitWriter(inputLength / 2 + 16);

    writer.WriteBits(1, 1); // BFINAL = 1
    writer.WriteBits(1, 2); // BTYPE = 01 (fixed Huffman)

    foreach (Token t in tokens)
    {
      if (t.Dist == 0)
      {
        WriteHuffman(writer, FixedLitLen[t.LitOrLen]);
        continue;
      }

      (byte lenCode, byte lenExtraBits, ushort lenExtraVal) = LenEncode[t.LitOrLen];
      WriteHuffman(writer, FixedLitLen[257 + lenCode]);
      if (lenExtraBits != 0)
        writer.WriteBits(lenExtraVal, lenExtraBits);

      (byte distCode, byte distExtraBits, ushort distExtraVal) = DistEncode[t.Dist];
      WriteHuffman(writer, FixedDist[distCode]);
      if (distExtraBits != 0)
        writer.WriteBits(distExtraVal, distExtraBits);
    }

    WriteHuffman(writer, FixedLitLen[256]); // EOB
    writer.Flush();
    return writer.ToArray();
  }

  /// <summary>
  /// Пишет код Хаффмана: канонический код хранится MSB-first, в поток DEFLATE он идёт
  /// «старший бит первым», поэтому при LSB-first записи его биты разворачиваются.
  /// </summary>
  private static void WriteHuffman(BitWriter writer, (int Code, int Len) c)
      => writer.WriteBits((uint)ReverseBits(c.Code, c.Len), c.Len);

  // ============================================================
  // Stored-блоки (для несжимаемых данных)
  // ============================================================

  private static long ComputeStoredSize(int length)
  {
    if (length == 0)
      return 5; // один пустой stored-блок

    int blocks = (length + 65534) / 65535;
    return (long)blocks * 5 + length;
  }

  private static byte[] EncodeStored(ReadOnlySpan<byte> input)
  {
    int n = input.Length;
    int blocks = n == 0 ? 1 : (n + 65534) / 65535;

    var output = new byte[ComputeStoredSize(n)];
    int outPos = 0;
    int offset = 0;

    for (int b = 0; b < blocks; b++)
    {
      int len = Math.Min(65535, n - offset);
      bool last = b == blocks - 1;

      // BFINAL + BTYPE(00). Stored-блок выровнен по байту, поэтому это один байт.
      output[outPos++] = (byte)(last ? 1 : 0);

      output[outPos++] = (byte)len;
      output[outPos++] = (byte)(len >> 8);
      output[outPos++] = (byte)~len;
      output[outPos++] = (byte)(~len >> 8);

      input.Slice(offset, len).CopyTo(output.AsSpan(outPos));
      outPos += len;
      offset += len;
    }

    return output;
  }

  // ============================================================
  // Bit writer (LSB-first), как зеркало декодера
  // ============================================================

  private sealed class BitWriter
  {
    private readonly List<byte> _bytes;
    private int _bitBuffer;
    private int _bitCount;

    public BitWriter(int capacityHint) => _bytes = new List<byte>(Math.Max(16, capacityHint));

    public void WriteBits(uint value, int count)
    {
      _bitBuffer |= (int)((value & ((1u << count) - 1)) << _bitCount);
      _bitCount += count;
      while (_bitCount >= 8)
      {
        _bytes.Add((byte)_bitBuffer);
        _bitBuffer >>= 8;
        _bitCount -= 8;
      }
    }

    public void Flush()
    {
      if (_bitCount > 0)
      {
        _bytes.Add((byte)_bitBuffer);
        _bitBuffer = 0;
        _bitCount = 0;
      }
    }

    public byte[] ToArray() => [.. _bytes];
  }

  private static int ReverseBits(int code, int len)
  {
    int result = 0;
    for (int i = 0; i < len; i++)
    {
      result = (result << 1) | (code & 1);
      code >>= 1;
    }

    return result;
  }

  // ============================================================
  // Построение таблиц
  // ============================================================

  private static (byte, byte, ushort)[] BuildLengthEncode()
  {
    var table = new (byte, byte, ushort)[MaxMatch + 1];
    for (int code = 0; code < LengthBase.Length; code++)
    {
      int baseLen = LengthBase[code];
      int extra = LengthExtra[code];
      int count = code == LengthBase.Length - 1 ? 1 : (1 << extra);
      for (int j = 0; j < count; j++)
      {
        int len = baseLen + j;
        if (len <= MaxMatch)
          table[len] = ((byte)code, (byte)extra, (ushort)j);
      }
    }

    return table;
  }

  private static (byte, byte, ushort)[] BuildDistEncode()
  {
    var table = new (byte, byte, ushort)[WindowSize + 1];
    for (int code = 0; code < DistBase.Length; code++)
    {
      int baseDist = DistBase[code];
      int extra = DistExtra[code];
      int count = 1 << extra;
      for (int j = 0; j < count; j++)
      {
        int dist = baseDist + j;
        if (dist <= WindowSize)
          table[dist] = ((byte)code, (byte)extra, (ushort)j);
      }
    }

    return table;
  }

  private static (int, int)[] BuildFixedLitLen()
  {
    int[] lengths = new int[288];
    for (int i = 0; i < 144; i++) lengths[i] = 8;
    for (int i = 144; i < 256; i++) lengths[i] = 9;
    for (int i = 256; i < 280; i++) lengths[i] = 7;
    for (int i = 280; i < 288; i++) lengths[i] = 8;

    return BuildCanonical(lengths);
  }

  private static (int, int)[] BuildFixedDist()
  {
    int[] lengths = new int[30];
    for (int i = 0; i < 30; i++) lengths[i] = 5;

    return BuildCanonical(lengths);
  }

  /// <summary>
  /// Строит канонические коды Хаффмана (MSB-first) по длинам кодов (RFC 1951, §3.2.2).
  /// </summary>
  private static (int Code, int Len)[] BuildCanonical(int[] lengths)
  {
    const int maxBits = 15;
    Span<int> blCount = stackalloc int[maxBits + 1];
    foreach (int l in lengths)
      if (l != 0)
        blCount[l]++;

    Span<int> nextCode = stackalloc int[maxBits + 1];
    int code = 0;
    for (int bits = 1; bits <= maxBits; bits++)
    {
      code = (code + blCount[bits - 1]) << 1;
      nextCode[bits] = code;
    }

    var result = new (int, int)[lengths.Length];
    for (int n = 0; n < lengths.Length; n++)
    {
      int len = lengths[n];
      if (len != 0)
        result[n] = (nextCode[len]++, len);
    }

    return result;
  }
}
