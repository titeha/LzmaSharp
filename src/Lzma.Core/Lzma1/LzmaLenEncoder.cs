namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Кодер длины для LZMA (len / repLen).</para>
/// <para>
/// В LZMA длина (len) кодируется через дерево решений:
/// <list type="bullet">
/// <item><description><c>choice = 0</c>  =&gt; <c>low[posState]</c>  (3 бита, 0..7)</description></item>
/// <item><description><c>choice = 1</c>, <c>choice2 = 0</c> =&gt; <c>mid[posState]</c> (3 бита, 0..7)</description></item>
/// <item><description><c>choice = 1</c>, <c>choice2 = 1</c> =&gt; <c>high</c> (8 бит, 0..255)</description></item>
/// </list>
/// </para>
/// <para>
/// На вход подаётся <b>реальная длина совпадения</b> (в байтах), т.е. <c>len &gt;= MatchMinLen</c>.
/// Внутри кодер переводит её в «символ длины» (<c>len - MatchMinLen</c>).
/// </para>
/// <para>
/// Почему отдельный класс?
/// - чтобы «кирпичики» энкодера можно было тестировать по отдельности;
/// - чтобы потом собирать полноценный LZMA-энкодер маленькими шагами.
/// </para>
/// </summary>
internal sealed class LzmaLenEncoder
{
  // choice[0] = choice, choice[1] = choice2
  private readonly ushort[] _choice = new ushort[2];

  private readonly LzmaBitTreeEncoder[] _low;
  private readonly LzmaBitTreeEncoder[] _mid;
  private readonly LzmaBitTreeEncoder _high;

  private readonly int _posStateCount;

  public LzmaLenEncoder(int posStateCount)
  {
    if (posStateCount <= 0)
      throw new ArgumentOutOfRangeException(nameof(posStateCount), "posStateCount должен быть > 0.");

    if (posStateCount > LzmaConstants.LenNumPosStatesMax)
      throw new ArgumentOutOfRangeException(
        nameof(posStateCount),
        $"posStateCount должен быть <= {LzmaConstants.LenNumPosStatesMax}.");

    _posStateCount = posStateCount;

    _low = new LzmaBitTreeEncoder[posStateCount];
    _mid = new LzmaBitTreeEncoder[posStateCount];

    for (int i = 0; i < posStateCount; i++)
    {
      _low[i] = new LzmaBitTreeEncoder(LzmaConstants.LenNumLowBits);
      _mid[i] = new LzmaBitTreeEncoder(LzmaConstants.LenNumMidBits);
    }

    _high = new LzmaBitTreeEncoder(LzmaConstants.LenNumHighBits);

    Reset();
  }

  /// <summary>
  /// Сбрасывает вероятности в начальное состояние.
  /// Вызывать перед началом кодирования нового потока.
  /// </summary>
  public void Reset()
  {
    LzmaProbability.Reset(_choice);

    for (int i = 0; i < _posStateCount; i++)
    {
      _low[i].Reset();
      _mid[i].Reset();
    }

    _high.Reset();
  }

  /// <summary>
  /// Кодирует длину <paramref name="len"/> для указанного <paramref name="posState"/>.
  /// </summary>
  /// <param name="range">Range encoder, куда пишем биты.</param>
  /// <param name="posState">Состояние позиции (обычно <c>pos &amp; posStateMask</c>).</param>
  /// <param name="len">Реальная длина совпадения (в байтах), <c>&gt;= MatchMinLen</c>.</param>
  public void Encode(ref LzmaRangeEncoder range, int posState, int len)
  {
    if ((uint)posState >= (uint)_posStateCount)
      throw new ArgumentOutOfRangeException(nameof(posState));

    if (len < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(len), $"len должен быть >= {LzmaConstants.MatchMinLen}.");

    int symbol = len - LzmaConstants.MatchMinLen;

    // low
    if (symbol < LzmaConstants.LenNumLowSymbols)
    {
      range.EncodeBit(ref _choice[0], 0u);
      _low[posState].EncodeSymbol(range, (uint)symbol);
      return;
    }

    // mid / high
    range.EncodeBit(ref _choice[0], 1u);
    symbol -= LzmaConstants.LenNumLowSymbols;

    if (symbol < LzmaConstants.LenNumMidSymbols)
    {
      range.EncodeBit(ref _choice[1], 0u);
      _mid[posState].EncodeSymbol(range, (uint)symbol);
      return;
    }

    range.EncodeBit(ref _choice[1], 1u);
    symbol -= LzmaConstants.LenNumMidSymbols;

    if ((uint)symbol >= (uint)LzmaConstants.LenNumHighSymbols)
      throw new ArgumentOutOfRangeException(nameof(len), "len слишком большой для кодера длины LZMA.");

    _high.EncodeSymbol(range, (uint)symbol);
  }
}
