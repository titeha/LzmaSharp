using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>Мини-энкодер для тестов: кодирует поток, где есть хотя бы один обычный match (isMatch=1, isRep=0).</para>
/// <para>
/// Мы делаем это исключительно для unit-тестов, чтобы проверить, что наш декодер
/// умеет разбирать ветку match и корректно копировать данные из словаря.
/// </para>
/// <para>
/// Ограничения:
/// - используем только distance=1 (то есть повтор последнего байта), чтобы не тянуть
///   сложную часть кодирования дистанции;
/// - используем только «обычный match» (isRep=0);
/// - используем только «low» ветку LenDecoder (длины 2..9).
/// </para>
/// </summary>
internal static class LzmaTestSimpleMatchEncoder
{
  private const int LiteralCoderSize = 0x300;

  /// <summary>
  /// Синоним для удобства: кодирует тот же поток, что и Encode_A_Run_With_One_Match.
  /// </summary>
  public static byte[] Encode(LzmaProperties props, int totalLen) =>
    Encode_A_Run_With_One_Match(props, totalLen);

  /// <summary>
  /// Кодирует поток, который распаковывается в строку из <paramref name="totalLen"/> байт 'A'.
  /// Первый байт — литерал 'A', остальное — один match с distance=1.
  /// </summary>
  public static byte[] Encode_A_Run_With_One_Match(LzmaProperties props, int totalLen)
  {
    if (totalLen < 2)
      throw new ArgumentOutOfRangeException(nameof(totalLen));

    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // isMatch[state][posState]
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    LzmaProbability.Reset(isMatch);

    // isRep[state]
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    LzmaProbability.Reset(isRep);

    // LenDecoder probs
    var len = new LenEncoderForTests(numPosStates);

    // posSlotDecoders[lenToPosState] (каждый по 6 бит)
    var posSlot = new BitTreeEncoderForTests(LzmaConstants.NumPosSlotBits);

    // literal probs
    int numLiteralContexts = 1 << (props.Lc + props.Lp);
    ushort[] literalProbs = new ushort[LiteralCoderSize * numLiteralContexts];
    LzmaProbability.Reset(literalProbs);

    var state = new LzmaState();
    state.Reset();

    byte prev = 0;
    var range = new LzmaTestRangeEncoder();

    // 1) Первый байт — литерал 'A'
    {
      long pos = 0;
      int posState = (int)pos & posStateMask;
      ref ushort pIsMatch = ref isMatch[state.Value * numPosStates + posState];
      range.EncodeBit(ref pIsMatch, 0);

      EncodeLiteral(range, props, literalProbs, pos, prev, (byte)'A');
      prev = (byte)'A';
      state.UpdateLiteral();
    }

    // 2) Один match (isMatch=1, isRep=0)
    {
      long pos = 1;
      int posState = (int)pos & posStateMask;

      ref ushort pIsMatch = ref isMatch[state.Value * numPosStates + posState];
      range.EncodeBit(ref pIsMatch, 1);

      ref ushort pIsRep = ref isRep[state.Value];
      range.EncodeBit(ref pIsRep, 0);

      // Длина матча = totalLen - 1 (первый литерал уже есть)
      uint matchLen = (uint)(totalLen - 1);
      if (matchLen < LzmaConstants.MatchMinLen || matchLen > 9)
        throw new InvalidOperationException("Этот тестовый энкодер поддерживает только длины 2..9 (low ветка).");

      len.EncodeLowOnly(range, posState, matchLen);

      // distance=1 => posSlot=0, дополнительных бит нет.
      // lenToPosState вычисляется как в декодере: min(len-2, 3)
      int lenToPosState = (int)(matchLen < 6 ? (matchLen - 2) : 3);
      if (lenToPosState != 3)
      {
        // Для простоты закрепим на lenToPosState=3 (это самый «общий» вариант).
        // При необходимости расширим.
        throw new InvalidOperationException("Для данного теста ожидаем lenToPosState == 3.");
      }

      // Кодируем posSlot=0 как 6 нулевых бит (symbol=0)
      posSlot.EncodeSymbol(range, 0);

      // После матча состояние обновится как match
      state.UpdateMatch();
      prev = (byte)'A';
    }

    return range.Finish();
  }

  private static void EncodeLiteral(
      LzmaTestRangeEncoder range,
      LzmaProperties props,
      ushort[] literalProbs,
      long pos,
      byte prevByte,
      byte value)
  {
    int ctx = CalcLiteralContext(props, pos, prevByte);
    int baseIndex = ctx * LiteralCoderSize;

    int symbol = 1;
    for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
    {
      uint bit = (uint)((value >> bitIndex) & 1);
      ref ushort prob = ref literalProbs[baseIndex + symbol];
      range.EncodeBit(ref prob, bit);
      symbol = (symbol << 1) | (int)bit;
    }
  }

  private static int CalcLiteralContext(LzmaProperties props, long pos, byte prevByte)
  {
    int lpMask = (1 << props.Lp) - 1;
    int a = ((int)pos & lpMask) << props.Lc;
    int b = prevByte >> (8 - props.Lc);
    return a + b;
  }

  private sealed class BitTreeEncoderForTests
  {
    private readonly ushort[] _probs;
    private readonly int _numBits;

    public BitTreeEncoderForTests(int numBits)
    {
      _numBits = numBits;
      _probs = new ushort[1 << numBits];
      LzmaProbability.Reset(_probs);
    }

    public void EncodeSymbol(LzmaTestRangeEncoder range, uint symbol)
    {
      uint m = 1;
      for (int i = _numBits - 1; i >= 0; i--)
      {
        uint bit = (symbol >> i) & 1;
        ref ushort p = ref _probs[m];
        range.EncodeBit(ref p, bit);
        m = (m << 1) | bit;
      }
    }
  }

  private sealed class LenEncoderForTests
  {
    private readonly ushort[] _choice = new ushort[2];
    private readonly BitTreeEncoderForTests[] _low;
    private readonly int _posStateCount;

    public LenEncoderForTests(int posStateCount)
    {
      _posStateCount = posStateCount;
      _low = new BitTreeEncoderForTests[LzmaConstants.NumPosStatesMax];
      for (int i = 0; i < LzmaConstants.NumPosStatesMax; i++)
        _low[i] = new BitTreeEncoderForTests(LzmaConstants.LenNumLowBits);

      Reset();
    }

    private void Reset()
    {
      _choice[0] = LzmaProbability.Initial;
      _choice[1] = LzmaProbability.Initial;
    }

    public void EncodeLowOnly(LzmaTestRangeEncoder range, int posState, uint matchLen)
    {
      if (posState < 0 || posState >= _posStateCount)
        throw new ArgumentOutOfRangeException(nameof(posState));

      // choice0 = 0 (low)
      range.EncodeBit(ref _choice[0], 0);

      uint sym = matchLen - LzmaConstants.MatchMinLen;
      _low[posState].EncodeSymbol(range, sym);
    }
  }
}
