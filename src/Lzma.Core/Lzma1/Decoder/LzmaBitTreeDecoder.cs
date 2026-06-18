namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодер бинарного дерева (BitTree) из LZMA SDK.</para>
/// <para>
/// BitTree — это способ декодировать число из N бит через последовательность
/// вызовов RangeDecoder.TryDecodeBit(), где вероятность для каждого бита хранится
/// в узле дерева.
/// </para>
/// <para>
/// Важно:
/// - Этот класс намеренно простой (без "магии" и без оптимизаций).
/// - Метод <see cref="TryDecodeSymbol"/> не является "атомарным": если на каком-то
///   шаге RangeDecoder вернёт <see cref="LzmaRangeDecodeResult.NeedMoreInput"/>,
///   то часть бит уже может быть декодирована, а вероятности — изменены.
///   На ранних шагах разработки мы используем его там, где вход целиком в памяти,
///   поэтому NeedMoreInput трактуем как повреждение/обрыв потока.
/// </para>
/// </summary>
public sealed class LzmaBitTreeDecoder
{
  private readonly int _numBits;
  private readonly ushort[] _probs;

  /// <param name="numBits">Количество бит в декодируемом символе (обычно 4..6 в LZMA).</param>
  public LzmaBitTreeDecoder(int numBits)
  {
    if (numBits <= 0)
      throw new ArgumentOutOfRangeException(nameof(numBits), "numBits должен быть > 0.");
    if (numBits > 30)
      throw new ArgumentOutOfRangeException(nameof(numBits), "numBits слишком большой (1<<numBits не поместится в int).");

    _numBits = numBits;
    _probs = new ushort[1 << numBits];

    Reset();
  }

  /// <summary>Количество бит в декодируемом символе.</summary>
  public int NumBits => _numBits;

  /// <summary>Размер массива вероятностей.</summary>
  public int ProbabilityCount => _probs.Length;

  /// <summary>
  /// Вернуть вероятность из внутреннего массива.
  /// Индекс 0 не используется BitTree, но мы всё равно инициализируем его.
  /// </summary>
  public ushort GetProbability(int index)
  {
    if ((uint)index >= (uint)_probs.Length)
      throw new ArgumentOutOfRangeException(nameof(index));
    return _probs[index];
  }

  /// <summary>
  /// Сбросить все вероятности в начальное состояние.
  /// </summary>
  public void Reset() => LzmaProbability.Reset(_probs);

  /// <summary>
  /// Декодировать символ из <see cref="NumBits"/> бит.
  ///
  /// Алгоритм соответствует LZMA SDK:
  ///   m = 1
  ///   for i in 0..NumBits-1:
  ///     bit = DecodeBit(probs[m])
  ///     m = (m << 1) + bit
  ///   symbol = m - (1 << NumBits)
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeSymbol(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      out uint symbol)
  {
    uint m = 1;

    for (int i = 0; i < _numBits; i++)
    {
      int probIndex = (int)m;

      var res = range.TryDecodeBit(ref _probs[probIndex], input, ref inputOffset, out uint bit);
      if (res != LzmaRangeDecodeResult.Ok)
      {
        symbol = 0;
        return res;
      }

      m = (m << 1) + bit;
    }

    symbol = m - (uint)(1 << _numBits);
    return LzmaRangeDecodeResult.Ok;
  }

  /// <summary>
  /// Декодировать символ из <see cref="NumBits"/> бит, но в "обратном" порядке (LSB-first).
  ///
  /// Используется в LZMA для некоторых полей дистанции (reverse bit tree).
  ///
  /// Алгоритм соответствует LZMA SDK:
  ///   m = 1
  ///   symbol = 0
  ///   for i in 0..NumBits-1:
  ///     bit = DecodeBit(probs[m])
  ///     m = (m << 1) + bit
  ///     symbol |= bit << i
  /// </summary>
  public LzmaRangeDecodeResult TryReverseDecode(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      out uint symbol)
  {
    uint m = 1;
    uint resSymbol = 0;

    for (int i = 0; i < _numBits; i++)
    {
      int probIndex = (int)m;

      var res = range.TryDecodeBit(ref _probs[probIndex], input, ref inputOffset, out uint bit);
      if (res != LzmaRangeDecodeResult.Ok)
      {
        symbol = 0;
        return res;
      }

      m = (m << 1) + bit;
      resSymbol |= (bit << i);
    }

    symbol = resSymbol;
    return LzmaRangeDecodeResult.Ok;
  }
}
