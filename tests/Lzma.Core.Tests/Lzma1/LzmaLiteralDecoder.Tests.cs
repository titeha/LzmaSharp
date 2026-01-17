using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaLiteralDecoderTests
{
  private static LzmaRangeDecoder InitWith(ReadOnlySpan<byte> initBytes)
  {
    if (initBytes.Length != 5)
      throw new ArgumentException("Для инициализации range decoder нужно ровно 5 байт.", nameof(initBytes));

    var range = new LzmaRangeDecoder();
    int offset = 0;
    var res = range.TryInitialize(initBytes, ref offset);
    Assert.Equal(LzmaRangeInitResult.Ok, res);
    Assert.Equal(5, offset);
    return range;
  }

  [Fact]
  public void ContextCount_Равен1СдвигLcPlusLp()
  {
    var dec = new LzmaLiteralDecoder(lc: 3, lp: 1);
    Assert.Equal(1 << (3 + 1), dec.ContextCount);
  }

  [Theory]
  [InlineData(0, 0, 0, (byte)0xAB, 0)]
  [InlineData(2, 1, 3, (byte)0b1011_0011, 6)]
  [InlineData(3, 0, 5, (byte)0b1111_0000, 7)]
  public void ComputeContextIndex_СчитаетсяПоФормуле(int lc, int lp, int position, byte prev, int expected)
  {
    var dec = new LzmaLiteralDecoder(lc, lp);
    int ctx = dec.ComputeContextIndex(position, prev);
    Assert.Equal(expected, ctx);
  }

  [Fact]
  public void TryDecodeNormal_НулевойПоток_ДекодируетБайт00()
  {
    var dec = new LzmaLiteralDecoder(lc: 0, lp: 0);
    var range = InitWith([0, 0, 0, 0, 0]);

    int inputOffset = 0;
    var res = dec.TryDecodeNormal(
        range,
        input: ReadOnlySpan<byte>.Empty,
        ref inputOffset,
        previousByte: 0,
        position: 0,
        out byte b);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(0, inputOffset);
    Assert.Equal((byte)0x00, b);
  }

  [Fact]
  public void TryDecodeNormal_ПотокИзFF_ДекодируетБайтFF()
  {
    var dec = new LzmaLiteralDecoder(lc: 0, lp: 0);
    var range = InitWith([0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

    int inputOffset = 0;
    var res = dec.TryDecodeNormal(
        range,
        input: ReadOnlySpan<byte>.Empty,
        ref inputOffset,
        previousByte: 0,
        position: 0,
        out byte b);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(0, inputOffset);
    Assert.Equal((byte)0xFF, b);
  }

  [Fact]
  public void TryDecodeWithMatchByte_НулевойПотокИMatch00_ДекодируетБайт00()
  {
    var dec = new LzmaLiteralDecoder(lc: 0, lp: 0);
    var range = InitWith([0, 0, 0, 0, 0]);

    int inputOffset = 0;
    var res = dec.TryDecodeWithMatchByte(
        range,
        input: ReadOnlySpan<byte>.Empty,
        ref inputOffset,
        previousByte: 0,
        position: 0,
        matchByte: 0x00,
        out byte b);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(0, inputOffset);
    Assert.Equal((byte)0x00, b);
  }

  [Fact]
  public void TryDecodeWithMatchByte_ПотокИзFFИMatchFF_ДекодируетБайтFF()
  {
    var dec = new LzmaLiteralDecoder(lc: 0, lp: 0);
    var range = InitWith([0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

    int inputOffset = 0;
    var res = dec.TryDecodeWithMatchByte(
        range,
        input: ReadOnlySpan<byte>.Empty,
        ref inputOffset,
        previousByte: 0,
        position: 0,
        matchByte: 0xFF,
        out byte b);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(0, inputOffset);
    Assert.Equal((byte)0xFF, b);
  }

  [Fact]
  public void Вероятности_РазныеКонтексты_НеВмешиваютсяДругВДруга()
  {
    // lc=0, lp=1 -> ровно 2 контекста (позиция чёт/нечёт).
    var dec = new LzmaLiteralDecoder(lc: 0, lp: 1);
    Assert.Equal(2, dec.ContextCount);

    // До декодирования вероятность в узле 1 одинаковая.
    Assert.Equal(LzmaConstants.ProbabilityInitValue, dec.GetProbability(contextIndex: 0, probabilityIndex: 1));
    Assert.Equal(LzmaConstants.ProbabilityInitValue, dec.GetProbability(contextIndex: 1, probabilityIndex: 1));

    var range = InitWith([0, 0, 0, 0, 0]);
    int inputOffset = 0;

    // Декодируем один байт в контексте 0 (position=0).
    var res = dec.TryDecodeNormal(
        range,
        input: ReadOnlySpan<byte>.Empty,
        ref inputOffset,
        previousByte: 0,
        position: 0,
        out _);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);

    // В контексте 0 вероятность в узле 1 должна измениться (bit=0 => prob увеличится).
    Assert.True(dec.GetProbability(0, 1) > LzmaConstants.ProbabilityInitValue);

    // В контексте 1 вероятность не трогали.
    Assert.Equal(LzmaConstants.ProbabilityInitValue, dec.GetProbability(1, 1));
  }
}
