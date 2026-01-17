namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодер литералов LZMA.</para>
/// <para>
/// Здесь мы декодируем один байт (0..255) на основе контекста:
/// - lc: сколько старших бит прошлого байта учитываем;
/// - lp: сколько младших бит позиции (pos) учитываем.
/// </para>
/// <para>
/// Важно: этот класс — "строительный блок" декодера. В полном LZMA-декодере
/// литерал декодируется после того, как isMatch == 0.
/// </para>
/// </summary>
public sealed class LzmaLiteralDecoder
{
  // По LZMA SDK: один "subcoder" имеет 0x300 вероятностей.
  private const int _literalCoderSize = 0x300;

  private readonly int _lc;
  private readonly int _lp;

  internal int ContextCount => 1 << (_lc + _lp);

  public LzmaLiteralDecoder(int lc, int lp)
  {
    if (lc < 0 || lc > LzmaProperties.MaxLc)
      throw new ArgumentOutOfRangeException(nameof(lc));
    if (lp < 0 || lp > LzmaProperties.MaxLp)
      throw new ArgumentOutOfRangeException(nameof(lp));

    _lc = lc;
    _lp = lp;

    // Количество контекстов = 2^(lc+lp)
    int numContexts = 1 << (lc + lp);

    // В каждом контексте LiteralCoderSize вероятностей.
    Probs = new ushort[numContexts * _literalCoderSize];

    Reset();
  }

  /// <summary>
  /// Сбрасывает вероятности литералов в исходное значение.
  /// </summary>
  public void Reset()
  {
    LzmaProbability.Reset(Probs);
  }

  public LzmaRangeDecodeResult TryDecodeNormal(
      LzmaRangeDecoder rangeDecoder,
      ReadOnlySpan<byte> input,
      ref int offset,
      byte previousByte,
      long position,
      out byte decoded)
  {
    int ctx = GetContextIndex(position, previousByte);
    int baseIndex = GetSubCoderOffset(ctx);

    int symbol = 1;

    for (int i = 0; i < 8; i++)
    {
      ref ushort prob = ref Probs[baseIndex + symbol];

      var res = rangeDecoder.TryDecodeBit(ref prob, input, ref offset, out uint bit);
      if (res == LzmaRangeDecodeResult.NeedMoreInput)
      {
        decoded = 0;
        return res;
      }

      symbol = (symbol << 1) | (int)bit;
    }

    decoded = (byte)symbol;
    return LzmaRangeDecodeResult.Ok;
  }

  public LzmaRangeDecodeResult TryDecodeWithMatchByte(
      LzmaRangeDecoder rangeDecoder,
      ReadOnlySpan<byte> input,
      ref int offset,
      byte previousByte,
      long position,
      byte matchByte,
      out byte decoded)
  {
    int ctx = GetContextIndex(position, previousByte);
    int baseIndex = GetSubCoderOffset(ctx);

    int symbol = 1;

    for (int i = 0; i < 8; i++)
    {
      // В "matched" режиме мы идём по дереву, используя matchByte как подсказку.
      uint matchBit = (uint)((matchByte >> 7) & 1);
      matchByte <<= 1;

      int probIndex = baseIndex + (1 + (int)matchBit) * 0x100 + symbol;

      ref ushort prob = ref Probs[probIndex];

      var res = rangeDecoder.TryDecodeBit(ref prob, input, ref offset, out uint bit);
      if (res == LzmaRangeDecodeResult.NeedMoreInput)
      {
        decoded = 0;
        return res;
      }

      symbol = (symbol << 1) | (int)bit;

      if (matchBit != bit)
      {
        // Дальше идём как normal.
        for (i++; i < 8; i++)
        {
          ref ushort prob2 = ref Probs[baseIndex + symbol];

          var res2 = rangeDecoder.TryDecodeBit(ref prob2, input, ref offset, out uint bit2);
          if (res2 == LzmaRangeDecodeResult.NeedMoreInput)
          {
            decoded = 0;
            return res2;
          }

          symbol = (symbol << 1) | (int)bit2;
        }

        decoded = (byte)symbol;
        return LzmaRangeDecodeResult.Ok;
      }
    }

    decoded = (byte)symbol;
    return LzmaRangeDecodeResult.Ok;
  }

  internal int ComputeContextIndex(long position, byte previousByte)
  {
    // lp: берём младшие lp бит позиции.
    int lpMask = (1 << _lp) - 1;
    int posBits = (int)(position & lpMask);

    // lc: берём старшие lc бит прошлого байта.
    int prevBits = previousByte >> (8 - _lc);

    return (posBits << _lc) + prevBits;
  }

  private int GetContextIndex(long position, byte previousByte)
  {
    // lp: берём младшие lp бит позиции.
    int lpMask = (1 << _lp) - 1;
    int posBits = (int)(position & lpMask);

    // lc: берём старшие lc бит прошлого байта.
    int prevBits = previousByte >> (8 - _lc);

    return (posBits << _lc) + prevBits;
  }

  private static int GetSubCoderOffset(int ctx)
  {
    return ctx * _literalCoderSize;
  }

  internal ushort GetProbability(int contextIndex, int probabilityIndex)
  {
    if (contextIndex < 0 || contextIndex >= ContextCount)
      throw new ArgumentOutOfRangeException(nameof(contextIndex));
    if (probabilityIndex < 0 || probabilityIndex >= _literalCoderSize)
      throw new ArgumentOutOfRangeException(nameof(probabilityIndex));

    return Probs[contextIndex * _literalCoderSize + probabilityIndex];
  }

  // --- Внутренние helpers для инкрементальных декодеров (следующий слой) ---

  /// <summary>
  /// Размер одного "subcoder" (контекста) — 0x300 вероятностей.
  /// </summary>
  internal const int _subCoderSize = _literalCoderSize;

  /// <summary>
  /// Прямой доступ к массиву вероятностей (ВНУТРЕННИЙ API).
  /// Используем, чтобы пошагово (по одному биту) декодировать литерал в более высоком уровне.
  /// </summary>
  internal ushort[] Probs { get; }

  /// <summary>
  /// Возвращает смещение subcoder'а (в массиве вероятностей) по текущей позиции и предыдущему байту.
  /// Это удобно для пошагового декодирования литерала.
  /// </summary>
  internal int GetSubCoderOffset(long position, byte previousByte)
  {
    int lpMask = (1 << _lp) - 1;
    int posBits = (int)(position & lpMask);
    int prevBits = previousByte >> (8 - _lc);

    int ctx = (posBits << _lc) + prevBits;
    return ctx * _literalCoderSize;
  }
}
