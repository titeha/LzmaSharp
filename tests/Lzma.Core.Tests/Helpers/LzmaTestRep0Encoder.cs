using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// Мини-«энкодер» для тестов, который генерирует очень маленькие,
/// полностью контролируемые LZMA-потоки.
///
/// Важно:
/// - это НЕ полноценный энкодер;
/// - он покрывает ровно те ветки, которые мы тестируем в декодере;
/// - все вероятностные модели обновляются так же, как в декодере,
///   поэтому тесты реально проверяют корректность логики декодирования.
/// </summary>
internal static class LzmaTestRep0Encoder
{
  public static byte[] Encode_OneLiteral_Then_ShortRep0(LzmaProperties props, byte literal)
  {
    // Поток: init(5 байт) + literal + rep0 short
    // rep0 short кодируется через isRep0Long == 0 и копирует 1 байт из словаря.

    var enc = new LzmaTestRangeEncoder();
    enc.EncodeInitBytes();

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    var isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    var isRep = new ushort[LzmaConstants.NumStates];
    var isRepG0 = new ushort[LzmaConstants.NumStates];
    var isRep0Long = new ushort[LzmaConstants.NumStates * numPosStates];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRep0Long);
    lit.Reset();

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;

    // 1) literal
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos: 0, bit: 0);
    EncodeLiteral(enc, lit, pos: 0, prevByte: prev, value: literal);
    state.UpdateLiteral();
    prev = literal;

    // 2) rep0 short
    long pos = 1;
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 0);

    // isRep0Long == 0 означает «короткий rep0» (len = 1)
    int posState = (int)pos & posStateMask;
    int rep0LongIndex = (state.Value * numPosStates) + posState;
    enc.EncodeBit(ref isRep0Long[rep0LongIndex], 0);

    // Важно: состояние в декодере после short rep0 обновляется отдельно.
    state.UpdateShortRep();

    enc.Finish();
    return enc.ToArrayAndReset();
  }

  public static byte[] Encode_OneLiteral_Then_LongRep0(LzmaProperties props, int repLen, byte literal)
  {
    // Поток: init(5 байт) + literal + rep0 long
    // rep0 long кодируется через isRep0Long == 1 и длину (repLen).

    if (repLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(repLen), "repLen должен быть >= MatchMinLen.");

    if (repLen > LzmaConstants.MatchMinLen + ((1 << LzmaConstants.LenNumLowBits) - 1))
      throw new ArgumentOutOfRangeException(nameof(repLen), "На данном шаге тестовый энкодер поддерживает только repLen в " +
        "low-диапазоне (до MatchMinLen + 7). Это намеренное упрощение для маленьких тестов.");

    var enc = new LzmaTestRangeEncoder();
    enc.EncodeInitBytes();

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    var isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    var isRep = new ushort[LzmaConstants.NumStates];
    var isRepG0 = new ushort[LzmaConstants.NumStates];
    var isRep0Long = new ushort[LzmaConstants.NumStates * numPosStates];

    // repLen
    var repLenChoice = new ushort[2];
    var repLenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRep0Long);
    LzmaProbability.Reset(repLenChoice);
    LzmaProbability.Reset(repLenLow);
    lit.Reset();

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;

    // 1) literal
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos: 0, bit: 0);
    EncodeLiteral(enc, lit, pos: 0, prevByte: prev, value: literal);
    state.UpdateLiteral();
    prev = literal;

    // 2) rep0 long
    long pos = 1;
    int posState = (int)pos & posStateMask;

    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 0);

    int rep0LongIndex = (state.Value * numPosStates) + posState;
    enc.EncodeBit(ref isRep0Long[rep0LongIndex], 1);

    // repLen: на этом шаге используем только выбор "low".
    enc.EncodeBit(ref repLenChoice[0], 0);

    int repLenSymbol = repLen - LzmaConstants.MatchMinLen;
    int lowOffset = posState * (1 << LzmaConstants.LenNumLowBits);
    EncodeBitTree(enc, repLenLow.AsSpan(lowOffset, 1 << LzmaConstants.LenNumLowBits),
      LzmaConstants.LenNumLowBits, repLenSymbol);

    state.UpdateRep();

    enc.Finish();
    return enc.ToArrayAndReset();
  }

  public static byte[] Encode_OneLiteral_Then_Rep1(LzmaProperties props, int repLen, byte literal)
  {
    // Поток: init(5 байт) + literal + rep1 (длинный)

    if (repLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(repLen), "repLen должен быть >= MatchMinLen.");

    if (repLen > LzmaConstants.MatchMinLen + ((1 << LzmaConstants.LenNumLowBits) - 1))
      throw new ArgumentOutOfRangeException(
        nameof(repLen),
        "На данном шаге тестовый энкодер поддерживает только repLen в " + "low-диапазоне (до MatchMinLen + 7). Это намеренное упрощение для маленьких тестов.");

    var enc = new LzmaTestRangeEncoder();
    enc.EncodeInitBytes();

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    var isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    var isRep = new ushort[LzmaConstants.NumStates];
    var isRepG0 = new ushort[LzmaConstants.NumStates];
    var isRepG1 = new ushort[LzmaConstants.NumStates];

    // repLen
    var repLenChoice = new ushort[2];
    var repLenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRepG1);
    LzmaProbability.Reset(repLenChoice);
    LzmaProbability.Reset(repLenLow);
    lit.Reset();

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;

    // 1) literal
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos: 0, bit: 0);
    EncodeLiteral(enc, lit, pos: 0, prevByte: prev, value: literal);
    state.UpdateLiteral();
    prev = literal;

    // 2) rep1
    long pos = 1;
    int posState = (int)pos & posStateMask;

    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 1);
    enc.EncodeBit(ref isRepG1[state.Value], 0);

    // repLen (low)
    enc.EncodeBit(ref repLenChoice[0], 0);

    int repLenSymbol = repLen - LzmaConstants.MatchMinLen;
    int lowOffset = posState * (1 << LzmaConstants.LenNumLowBits);
    EncodeBitTree(enc, repLenLow.AsSpan(lowOffset, 1 << LzmaConstants.LenNumLowBits),
      LzmaConstants.LenNumLowBits, repLenSymbol);

    state.UpdateRep();

    enc.Finish();
    return enc.ToArrayAndReset();
  }

  public static byte[] Encode_OneLiteral_Then_Rep2(LzmaProperties props, int repLen, byte literal)
  {
    // Поток: init(5 байт) + literal + rep2 (длинный)

    if (repLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(repLen), "repLen должен быть >= MatchMinLen.");

    if (repLen > LzmaConstants.MatchMinLen + ((1 << LzmaConstants.LenNumLowBits) - 1))
      throw new ArgumentOutOfRangeException(nameof(repLen), "На данном шаге тестовый энкодер поддерживает только repLen в " +
        "low-диапазоне (до MatchMinLen + 7). Это намеренное упрощение для маленьких тестов.");

    var enc = new LzmaTestRangeEncoder();
    enc.EncodeInitBytes();

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    var isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    var isRep = new ushort[LzmaConstants.NumStates];
    var isRepG0 = new ushort[LzmaConstants.NumStates];
    var isRepG1 = new ushort[LzmaConstants.NumStates];
    var isRepG2 = new ushort[LzmaConstants.NumStates];

    // repLen
    var repLenChoice = new ushort[2];
    var repLenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRepG1);
    LzmaProbability.Reset(isRepG2);
    LzmaProbability.Reset(repLenChoice);
    LzmaProbability.Reset(repLenLow);
    lit.Reset();

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;

    // 1) literal
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos: 0, bit: 0);
    EncodeLiteral(enc, lit, pos: 0, prevByte: prev, value: literal);
    state.UpdateLiteral();
    prev = literal;

    // 2) rep2
    long pos = 1;
    int posState = (int)pos & posStateMask;

    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 1);
    enc.EncodeBit(ref isRepG1[state.Value], 1);
    enc.EncodeBit(ref isRepG2[state.Value], 0);

    // repLen (low)
    enc.EncodeBit(ref repLenChoice[0], 0);

    int repLenSymbol = repLen - LzmaConstants.MatchMinLen;
    int lowOffset = posState * (1 << LzmaConstants.LenNumLowBits);
    EncodeBitTree(enc, repLenLow.AsSpan(lowOffset, 1 << LzmaConstants.LenNumLowBits),
      LzmaConstants.LenNumLowBits, repLenSymbol);

    state.UpdateRep();

    enc.Finish();
    return enc.ToArrayAndReset();
  }

  public static byte[] Encode_OneLiteral_Then_Rep3(LzmaProperties props, int repLen, byte literal)
  {
    // Поток: init(5 байт) + literal + rep3 (длинный)

    if (repLen < LzmaConstants.MatchMinLen)
      throw new ArgumentOutOfRangeException(nameof(repLen), "repLen должен быть >= MatchMinLen.");

    if (repLen > LzmaConstants.MatchMinLen + ((1 << LzmaConstants.LenNumLowBits) - 1))
      throw new ArgumentOutOfRangeException(nameof(repLen), "На данном шаге тестовый энкодер поддерживает только repLen в " +
        "low-диапазоне (до MatchMinLen + 7). Это намеренное упрощение для маленьких тестов.");

    var enc = new LzmaTestRangeEncoder();
    enc.EncodeInitBytes();

    var lit = new LzmaLiteralDecoder(props.Lc, props.Lp);

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    var isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    var isRep = new ushort[LzmaConstants.NumStates];
    var isRepG0 = new ushort[LzmaConstants.NumStates];
    var isRepG1 = new ushort[LzmaConstants.NumStates];
    var isRepG2 = new ushort[LzmaConstants.NumStates];

    // repLen
    var repLenChoice = new ushort[2];
    var repLenLow = new ushort[numPosStates * (1 << LzmaConstants.LenNumLowBits)];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);
    LzmaProbability.Reset(isRepG1);
    LzmaProbability.Reset(isRepG2);
    LzmaProbability.Reset(repLenChoice);
    LzmaProbability.Reset(repLenLow);
    lit.Reset();

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;

    // 1) literal
    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos: 0, bit: 0);
    EncodeLiteral(enc, lit, pos: 0, prevByte: prev, value: literal);
    state.UpdateLiteral();
    prev = literal;

    // 2) rep3
    long pos = 1;
    int posState = (int)pos & posStateMask;

    EncodeIsMatch(enc, isMatch, state, numPosStates, posStateMask, pos, bit: 1);
    enc.EncodeBit(ref isRep[state.Value], 1);
    enc.EncodeBit(ref isRepG0[state.Value], 1);
    enc.EncodeBit(ref isRepG1[state.Value], 1);
    enc.EncodeBit(ref isRepG2[state.Value], 1);

    // repLen (low)
    enc.EncodeBit(ref repLenChoice[0], 0);

    int repLenSymbol = repLen - LzmaConstants.MatchMinLen;
    int lowOffset = posState * (1 << LzmaConstants.LenNumLowBits);
    EncodeBitTree(enc, repLenLow.AsSpan(lowOffset, 1 << LzmaConstants.LenNumLowBits),
      LzmaConstants.LenNumLowBits, repLenSymbol);

    state.UpdateRep();

    enc.Finish();
    return enc.ToArrayAndReset();
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
}
