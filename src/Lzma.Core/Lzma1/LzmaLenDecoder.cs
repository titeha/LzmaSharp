namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодер длины для LZMA.</para>
/// <para>
/// В LZMA длина (match len) кодируется в диапазоне [MatchMinLen..MatchMaxLen]
/// и выбирается в три шага:
/// </para>
/// <para>
/// 1) choice0 == 0  => "low"  (короткие длины)
/// 2) choice0 == 1 и choice1 == 0 => "mid" (средние длины)
/// 3) choice0 == 1 и choice1 == 1 => "high" (длинные)
/// </para>
/// <para>
/// Для "low" и "mid" используется отдельное дерево (bit tree) для каждого posState.
/// Для "high" используется одно общее дерево.
/// </para>
/// <para>
/// Важно:
/// - Этот класс не хранит входной поток сам.
/// - Для работы ему нужен <see cref="LzmaRangeDecoder"/> и входной буфер.
/// - Если данных не хватает, методы возвращают <see cref="LzmaRangeDecodeResult.NeedMoreInput"/>.
/// </para>
/// </summary>
public sealed class LzmaLenDecoder
{
  // Вероятности выбора ветки.
  // _choice[0] = choice0, _choice[1] = choice1.
  private readonly ushort[] _choice = new ushort[2];

  // Для каждого posState свой декодер для "low" и "mid".
  private readonly LzmaBitTreeDecoder[] _low;
  private readonly LzmaBitTreeDecoder[] _mid;

  // "high" — общий.
  private readonly LzmaBitTreeDecoder _high;

  private int _posStateCount;

  public LzmaLenDecoder()
  {
    _low = new LzmaBitTreeDecoder[LzmaConstants.NumPosStatesMax];
    _mid = new LzmaBitTreeDecoder[LzmaConstants.NumPosStatesMax];

    for (int i = 0; i < LzmaConstants.NumPosStatesMax; i++)
    {
      _low[i] = new LzmaBitTreeDecoder(LzmaConstants.LenNumLowBits);
      _mid[i] = new LzmaBitTreeDecoder(LzmaConstants.LenNumMidBits);
    }

    _high = new LzmaBitTreeDecoder(LzmaConstants.LenNumHighBits);

    // По умолчанию — самый простой случай: pb = 0 => 1 posState.
    Reset(posStateCount: 1);
  }

  /// <summary>
  /// Текущее количество posState (обычно равно 1 &lt;&lt; pb).
  /// </summary>
  public int PosStateCount => _posStateCount;

  /// <summary>
  /// Сбрасывает все вероятности в начальное состояние.
  /// posStateCount должен быть в диапазоне [1..NumPosStatesMax].
  /// </summary>
  public void Reset(int posStateCount)
  {
    if (posStateCount < 1 || posStateCount > LzmaConstants.NumPosStatesMax)
      throw new ArgumentOutOfRangeException(nameof(posStateCount));

    _posStateCount = posStateCount;

    _choice[0] = LzmaProbability.Initial;
    _choice[1] = LzmaProbability.Initial;

    for (int posState = 0; posState < _posStateCount; posState++)
    {
      _low[posState].Reset();
      _mid[posState].Reset();
    }

    _high.Reset();
  }

  /// <summary>
  /// Пытается декодировать длину.
  /// </summary>
  /// <param name="range">Range decoder (арифметический декодер).</param>
  /// <param name="src">Входные байты (должны содержать данные range-кодера).</param>
  /// <param name="offset">Текущая позиция во входном буфере (будет увеличиваться по мере чтения).</param>
  /// <param name="posState">posState (0..PosStateCount-1).</param>
  /// <param name="length">Декодированная длина (только если результат Ok).</param>
  public LzmaRangeDecodeResult TryDecode(
    ref LzmaRangeDecoder range,
    ReadOnlySpan<byte> src,
    ref int offset,
    int posState,
    out uint length)
  {
    if ((uint)posState >= (uint)_posStateCount)
      throw new ArgumentOutOfRangeException(nameof(posState));

    length = 0;

    // choice0
    var res = range.TryDecodeBit(ref _choice[0], src, ref offset, out uint bit);
    if (res != LzmaRangeDecodeResult.Ok)
      return res;

    if (bit == 0)
    {
      // low
      res = _low[posState].TryDecodeSymbol(ref range, src, ref offset, out uint sym);
      if (res != LzmaRangeDecodeResult.Ok)
        return res;

      length = sym + LzmaConstants.MatchMinLen;
      return LzmaRangeDecodeResult.Ok;
    }

    // choice1
    res = range.TryDecodeBit(ref _choice[1], src, ref offset, out bit);
    if (res != LzmaRangeDecodeResult.Ok)
      return res;

    if (bit == 0)
    {
      // mid
      res = _mid[posState].TryDecodeSymbol(ref range, src, ref offset, out uint sym);
      if (res != LzmaRangeDecodeResult.Ok)
        return res;

      length = sym + LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols;
      return LzmaRangeDecodeResult.Ok;
    }

    // high
    res = _high.TryDecodeSymbol(ref range, src, ref offset, out uint high);
    if (res != LzmaRangeDecodeResult.Ok)
      return res;

    length = high + LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols;
    return LzmaRangeDecodeResult.Ok;
  }
}
