using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderLzmaChunkDictionaryCarryTests
{
  [Fact]
  public void Decode_OneShot_TwoLzmaChunks_SecondUsesDictionaryFromFirst()
  {
    // PB=0 keeps posState constant even when the second chunk starts at pos=1.
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 0);

    byte[] plain1 = [(byte)'A'];
    const int matchLen = 5;

    byte[] payload1 = LzmaTestLiteralOnlyEncoder.Encode(props, plain1);

    // Second chunk begins at overall output position == 1.
    byte[] payload2 = LzmaTestSimpleMatchEncoder.Encode_MatchOnly_Distance1_MatchLen5_9(
      props,
      pos: plain1.Length,
      matchLen: matchLen);

    byte[] lzma2Stream = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsResetStateThenEnd(
      props,
      firstLzmaPayload: payload1,
      firstUnpackSize: plain1.Length,
      secondLzmaPayload: payload2,
      secondUnpackSize: matchLen);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain1.Length + matchLen];
    var res = dec.Decode(lzma2Stream, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(lzma2Stream.Length, consumed);
    Assert.Equal(dst.Length, written);

    Assert.Equal([(byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A', (byte)'A'], dst);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 0);

    byte[] plain1 = [(byte)'A'];
    const int matchLen = 5;

    byte[] payload1 = LzmaTestLiteralOnlyEncoder.Encode(props, plain1);

    byte[] payload2 = LzmaTestSimpleMatchEncoder.Encode_MatchOnly_Distance1_MatchLen5_9(
      props,
      pos: plain1.Length,
      matchLen: matchLen);

    byte[] lzma2Stream = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsResetStateThenEnd(
      props,
      firstLzmaPayload: payload1,
      firstUnpackSize: plain1.Length,
      secondLzmaPayload: payload2,
      secondUnpackSize: matchLen);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    var expected = new byte[plain1.Length + matchLen];
    for (int i = 0; i < expected.Length; i++)
      expected[i] = (byte)'A';

    byte[] dst = new byte[expected.Length];

    int srcPos = 0;
    int dstPos = 0;

    int guard = 0;
    while (true)
    {
      if (++guard > 200_000)
        throw new InvalidOperationException("Слишком много итераций: возможен бесконечный цикл.");

      int srcChunk = Math.Min(2, lzma2Stream.Length - srcPos);
      ReadOnlySpan<byte> src = lzma2Stream.AsSpan(srcPos, srcChunk);

      int dstChunk = Math.Min(1, dst.Length - dstPos);
      Span<byte> outChunk = dst.AsSpan(dstPos, dstChunk);

      var res = dec.Decode(src, outChunk, out int consumed, out int written);

      if (consumed == 0 && written == 0 && res != Lzma2DecodeResult.Finished)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      srcPos += consumed;
      dstPos += written;

      if (res == Lzma2DecodeResult.Finished)
        break;

      if (srcPos > lzma2Stream.Length)
        throw new InvalidOperationException("Считали больше входных данных, чем есть.");

      if (dstPos > dst.Length)
        throw new InvalidOperationException("Записали больше выходных данных, чем буфер.");
    }

    Assert.Equal(lzma2Stream.Length, srcPos);
    Assert.Equal(dst.Length, dstPos);
    Assert.Equal(expected, dst);
  }
}
