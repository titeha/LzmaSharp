using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>Генератор "литерального" LZMA-потока (только для тестов).</para>
/// <para>
/// Что делаем:
/// - для каждого байта данных кодируем isMatch = 0;
/// - затем кодируем 8 бит литерала через Literal-дерево (symbol=1..0x1FF).
/// </para>
/// <para>
/// Это позволяет нам протестировать RangeDecoder, LiteralDecoder и базовую
/// интеграцию в LzmaDecoder без реализации матчей...
/// </para>
/// </summary>
internal static class LzmaTestLiteralOnlyEncoder
{
  private const int _literalCoderSize = 0x300;

  public static byte[] Encode(LzmaProperties props, ReadOnlySpan<byte> plain)
  {
    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // isMatch[state][posState]
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    LzmaProbability.Reset(isMatch);

    // probabilities for literals: 0x300 * (1 << (lc + lp))
    int numLiteralContexts = 1 << (props.Lc + props.Lp);
    ushort[] literalProbs = new ushort[_literalCoderSize * numLiteralContexts];
    LzmaProbability.Reset(literalProbs);

    var state = new LzmaState();
    state.Reset();

    byte prevByte = 0;
    var range = new LzmaTestRangeEncoder();

    for (int i = 0; i < plain.Length; i++)
    {
      long pos = i; // позиция в распакованном потоке
      int posState = (int)pos & posStateMask;

      // 1) isMatch = 0
      ref ushort isMatchProb = ref isMatch[state.Value * numPosStates + posState];
      range.EncodeBit(ref isMatchProb, 0);

      // 2) сам литерал
      int ctx = CalcLiteralContext(props, pos, prevByte);
      int baseIndex = ctx * _literalCoderSize;

      int symbol = 1;
      byte b = plain[i];

      for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
      {
        uint bit = (uint)((b >> bitIndex) & 1);
        ref ushort prob = ref literalProbs[baseIndex + symbol];
        range.EncodeBit(ref prob, bit);
        symbol = (symbol << 1) | (int)bit;
      }

      prevByte = b;
      state.UpdateLiteral();
    }

    return range.Finish();
  }

  private static int CalcLiteralContext(LzmaProperties props, long pos, byte prevByte)
  {
    // Формула идентична той, что в LzmaLiteralDecoder:
    // ctx = ((pos & ((1<<lp)-1)) << lc) + (prevByte >> (8 - lc))
    int lpMask = (1 << props.Lp) - 1;
    int a = ((int)pos & lpMask) << props.Lc;
    int b = prevByte >> (8 - props.Lc);
    return a + b;
  }
}
