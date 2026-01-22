using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Helpers;

/// <summary>
/// <para>
/// Мини-энкодер для тестов: кодирует только литералы (без match/rep),
/// но умеет разделять поток на два LZMA2 LZMA-чанка.
/// </para>
/// <para>
/// Важно:
/// - На границе чанков мы начинаем новый range encoder (значит, у каждого чанка свои init-байты range coder).
/// - При этом мы НЕ сбрасываем вероятностные модели и LZMA-состояние между чанками.
///   Это соответствует control 0x80..0x9F в LZMA2 (нет properties и нет resetState).
/// </para>
/// </summary>
internal static class LzmaTestChunkedLiteralEncoder
{
  public static (byte[] payload1, byte[] payload2) EncodeTwoLiteralChunks(
    LzmaProperties props,
    ReadOnlySpan<byte> plain1,
    ReadOnlySpan<byte> plain2)
  {
    int numPosStates = 1 << props.Pb;
    int posStateMask = numPosStates - 1;

    // Глобальные для обоих чанков модели/состояние.
    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    LzmaProbability.Reset(isMatch);

    var literal = new LzmaLiteralDecoder(lc: props.Lc, lp: props.Lp);
    literal.Reset();

    var state = new LzmaState();
    state.Reset();

    long position = 0;
    byte prevByte = 0;

    // Чанк №1
    var range1 = new LzmaTestRangeEncoder();
    EncodeLiteralRun(range1, plain1, ref position, ref prevByte, ref state, isMatch, literal, numPosStates, posStateMask);
    byte[] payload1 = range1.Finish();

    // Чанк №2 (новый range encoder, но те же модели/состояние)
    var range2 = new LzmaTestRangeEncoder();
    EncodeLiteralRun(range2, plain2, ref position, ref prevByte, ref state, isMatch, literal, numPosStates, posStateMask);
    byte[] payload2 = range2.Finish();

    return (payload1, payload2);
  }

  private static void EncodeLiteralRun(
    LzmaTestRangeEncoder range,
    ReadOnlySpan<byte> bytes,
    ref long position,
    ref byte prevByte,
    ref LzmaState state,
    ushort[] isMatch,
    LzmaLiteralDecoder literal,
    int numPosStates,
    int posStateMask)
  {
    for (int i = 0; i < bytes.Length; i++)
    {
      byte b = bytes[i];

      int posState = (int)position & posStateMask;
      int isMatchIndex = (state.Value * numPosStates) + posState;

      // isMatch = 0 => literal
      range.EncodeBit(ref isMatch[isMatchIndex], 0);

      int subCoderOffset = literal.GetSubCoderOffset(position, prevByte);

      int symbol = 1;
      for (int bitIndex = 7; bitIndex >= 0; bitIndex--)
      {
        uint bit = (uint)((b >> bitIndex) & 1);
        range.EncodeBit(ref literal.Probs[subCoderOffset + symbol], bit);
        symbol = (symbol << 1) | (int)bit;
      }

      state.UpdateLiteral();
      prevByte = b;
      position++;
    }
  }
}
