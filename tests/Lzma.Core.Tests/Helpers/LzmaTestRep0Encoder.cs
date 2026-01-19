using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>Минимальный «энкодер» для тестов, который позволяет получить потоки с rep0.</para>
/// <para>
/// Важно:
/// - это НЕ производственный энкодер;
/// - он покрывает только те ветки, которые нам нужны для unit-тестов;
/// - мы сознательно держим код простым и понятным.
/// </para>
/// </summary>
internal static class LzmaTestRep0Encoder
{
  private const int LiteralCoderSize = 0x300;

  /// <summary>
  /// Поток: один литерал 'A', затем короткий rep0 (len = 1).
  /// Ожидаемый результат распаковки: "AA".
  /// </summary>
  public static byte[] Encode_OneLiteral_Then_ShortRep0(LzmaProperties props, byte literal)
  {
    // Настройки позиции
    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // Модели вероятностей
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    ushort[] isRepG0 = new ushort[LzmaConstants.NumStates];
    ushort[] isRep0Long = new ushort[LzmaConstants.NumStates * numPosStates];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRep0Long);

    // Литералы (lc/lp)
    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);
    lit.Reset();

    var state = new LzmaState();
    byte previousByte = 0;
    long pos = 0;

    var enc = new LzmaTestRangeEncoder();

    // 1) Литерал
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 0);
    EncodeLiteral(enc, lit, pos, previousByte, literal);
    previousByte = literal;
    state.UpdateLiteral();
    pos++;

    // 2) rep0 (короткий)
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 1);
    enc.EncodeBit(ref isRep[state.Value], 1);       // isRep = 1
    enc.EncodeBit(ref isRepG0[state.Value], 0);     // isRepG0 = 0 (rep0)

    int posState = (int)pos & posStateMask;
    int rep0LongIndex = (state.Value * numPosStates) + posState;
    enc.EncodeBit(ref isRep0Long[rep0LongIndex], 0); // isRep0Long = 0 => короткий rep (len = 1)

    state.UpdateShortRep();
    pos++;

    return enc.Finish();
  }

  /// <summary>
  /// Поток: один литерал 'A', затем длинный rep0 (len >= 2).
  /// Ожидаемый результат распаковки: (1 + <paramref name="repLen"/>) байт 'A'.
  /// </summary>
  public static byte[] Encode_OneLiteral_Then_LongRep0(LzmaProperties props, byte literal, int repLen)
  {
    ArgumentOutOfRangeException.ThrowIfLessThan(repLen, LzmaConstants.MatchMinLen);

    // Мы кодируем только "low" ветку LenDecoder: длины 2..9
    if (repLen > (LzmaConstants.MatchMinLen + ((1 << LzmaConstants.LenNumLowBits) - 1)))
      throw new ArgumentOutOfRangeException(nameof(repLen), "Этот тестовый энкодер поддерживает только длины 2..9 (low-ветка).");

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // Модели вероятностей
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    ushort[] isRepG0 = new ushort[LzmaConstants.NumStates];
    ushort[] isRep0Long = new ushort[LzmaConstants.NumStates * numPosStates];

    // repLenDecoder модели
    ushort[] repLenChoice = new ushort[2];
    ushort[] repLenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRep0Long);
    LzmaProbability.Reset(repLenChoice);
    LzmaProbability.Reset(repLenLow);

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);
    lit.Reset();

    var state = new LzmaState();
    byte previousByte = 0;
    long pos = 0;

    var enc = new LzmaTestRangeEncoder();

    // 1) Литерал
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 0);
    EncodeLiteral(enc, lit, pos, previousByte, literal);
    previousByte = literal;
    state.UpdateLiteral();
    pos++;

    // 2) rep0 (длинный)
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 1);
    enc.EncodeBit(ref isRep[state.Value], 1);   // isRep = 1
    enc.EncodeBit(ref isRepG0[state.Value], 0); // isRepG0 = 0 (rep0)

    int posState = (int)pos & posStateMask;
    int rep0LongIndex = (state.Value * numPosStates) + posState;
    enc.EncodeBit(ref isRep0Long[rep0LongIndex], 1); // isRep0Long = 1 => длинный rep0

    // repLenDecoder: choice[0]=0 и low[posState]
    enc.EncodeBit(ref repLenChoice[0], 0);

    int lowSymbol = repLen - LzmaConstants.MatchMinLen; // 0..7
    Span<ushort> low = repLenLow.AsSpan(posState * (1 << LzmaConstants.LenNumLowBits), (1 << LzmaConstants.LenNumLowBits));
    EncodeBitTree(enc, low, LzmaConstants.LenNumLowBits, lowSymbol);

    state.UpdateRep();
    pos += repLen;

    return enc.Finish();
  }

  /// <summary>
  /// Поток: один литерал, затем "rep с isRepG0=1".
  /// В нашем декодере эта ветка пока не реализована, и должна вернуть NotImplemented.
  /// </summary>
  public static byte[] Encode_OneLiteral_Then_RepG0_Is_1(LzmaProperties props, byte literal)
  {
    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    ushort[] isRepG0 = new ushort[LzmaConstants.NumStates];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);
    lit.Reset();

    var state = new LzmaState();
    byte previousByte = 0;
    long pos = 0;

    var enc = new LzmaTestRangeEncoder();

    // 1) Литерал
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 0);
    EncodeLiteral(enc, lit, pos, previousByte, literal);
    previousByte = literal;
    state.UpdateLiteral();
    pos++;

    // 2) rep, но не rep0 (isRepG0 = 1)
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 1);

    return enc.Finish();
  }

  private static void EncodeIsMatch(
    LzmaTestRangeEncoder enc,
    ushort[] isMatch,
    LzmaState state,
    int numPosStates,
    int posStateMask,
    long pos,
    uint bit)
  {
    int posState = (int)pos & posStateMask;
    int idx = (state.Value * numPosStates) + posState;
    enc.EncodeBit(ref isMatch[idx], bit);
  }

  private static void EncodeLiteral(LzmaTestRangeEncoder enc, LzmaLiteralDecoder lit, long pos, byte prevByte, byte value)
  {
    int subOffset = lit.GetSubCoderOffset(pos, prevByte);
    int symbol = 1;

    for (int i = 0; i < 8; i++)
    {
      int bit = (value >> (7 - i)) & 1;
      enc.EncodeBit(ref lit.Probs[subOffset + symbol], (uint)bit);
      symbol = (symbol << 1) | bit;
    }
  }

  private static void EncodeBitTree(LzmaTestRangeEncoder enc, Span<ushort> probs, int numBits, int symbol)
  {
    int m = 1;
    for (int i = numBits - 1; i >= 0; i--)
    {
      int bit = (symbol >> i) & 1;
      enc.EncodeBit(ref probs[m], (uint)bit);
      m = (m << 1) | bit;
    }
  }

  /// <summary>
  /// Алиас: оставлен для читаемости тестов.
  /// </summary>
  public static byte[] Encode_OneLiteral_Then_RepG0_Is1(LzmaProperties props, byte literal)
    => Encode_OneLiteral_Then_RepG0_Is_1(props, literal);
}
