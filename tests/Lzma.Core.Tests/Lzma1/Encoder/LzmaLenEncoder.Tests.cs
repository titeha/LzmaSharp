using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaLenEncoderTests
{
  [Fact]
  public void RoundTrip_LowMidHigh_ГраницыДиапазонов_Ок()
  {
    // posStateCount специально берём > 1, чтобы проверить индексирование low/mid по posState.
    const int posStateCount = 4;
    const int posState = 2;

    int[] lengths =
    {
      // low: 2..9 (8 символов)
      LzmaConstants.MatchMinLen,
      LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols - 1,

      // mid: 10..17 (ещё 8 символов)
      LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols,
      LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols - 1,

      // high: 18..273
      LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols + LzmaConstants.LenNumMidSymbols,
      LzmaConstants.MatchMaxLen,
    };

    var lenEnc = new LzmaLenEncoder(posStateCount);
    var rangeEnc = new LzmaRangeEncoder();
    rangeEnc.Reset();

    foreach (int len in lengths)
      lenEnc.Encode(ref rangeEnc, posState, len);

    rangeEnc.Flush();
    byte[] payload = rangeEnc.ToArray();

    // Декодируем тем же алгоритмом, что будет делать LZMA-декодер.
    var rangeDec = new LzmaRangeDecoder();
    int offset = 0;
    Assert.Equal(LzmaRangeInitResult.Ok, rangeDec.TryInitialize(payload, ref offset));

    var lenDec = new LzmaLenDecoder();
    lenDec.Reset(posStateCount);

    foreach (int expected in lengths)
    {
      Assert.Equal(
        LzmaRangeDecodeResult.Ok,
        lenDec.TryDecode(ref rangeDec, payload, ref offset, posState, out uint actual));

      Assert.Equal((uint)expected, actual);
    }
  }

  [Fact]
  public void Encode_ПроверяетАргументы()
  {
    var lenEnc = new LzmaLenEncoder(posStateCount: 1);
    var rangeEnc = new LzmaRangeEncoder();
    rangeEnc.Reset();

    Assert.Throws<ArgumentOutOfRangeException>(
      () => lenEnc.Encode(ref rangeEnc, posState: 1, len: LzmaConstants.MatchMinLen));

    Assert.Throws<ArgumentOutOfRangeException>(
      () => lenEnc.Encode(ref rangeEnc, posState: 0, len: LzmaConstants.MatchMinLen - 1));
  }

  [Fact]
  public void Reset_ДелаетРезультатДетерминированным()
  {
    // Идея: после Reset() + Reset() range encoder'а поток байт должен совпасть.
    // Это важно для предсказуемости при разработке/отладке.

    int[] lengths =
    {
      LzmaConstants.MatchMinLen,
      LzmaConstants.MatchMinLen + 3,
      LzmaConstants.MatchMinLen + LzmaConstants.LenNumLowSymbols,
      LzmaConstants.MatchMaxLen,
    };

    var lenEnc = new LzmaLenEncoder(posStateCount: 1);
    var rangeEnc = new LzmaRangeEncoder();

    rangeEnc.Reset();
    foreach (int len in lengths)
      lenEnc.Encode(ref rangeEnc, posState: 0, len);
    rangeEnc.Flush();
    byte[] first = rangeEnc.ToArray();

    // Сбрасываем обе стороны.
    lenEnc.Reset();
    rangeEnc.Reset();

    foreach (int len in lengths)
      lenEnc.Encode(ref rangeEnc, posState: 0, len);
    rangeEnc.Flush();
    byte[] second = rangeEnc.ToArray();

    Assert.Equal(first, second);
  }
}
