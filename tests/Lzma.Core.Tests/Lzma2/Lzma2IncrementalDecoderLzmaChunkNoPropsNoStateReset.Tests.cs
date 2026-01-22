using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma2;

public class Lzma2IncrementalDecoderLzmaChunkNoPropsNoStateResetTests
{
  [Fact]
  public void Decode_SecondChunkWithoutPropsAndWithoutStateReset_Works()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] plain1 = [(byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E'];
    byte[] plain2 = [(byte)'F', (byte)'G', (byte)'H', (byte)'I'];

    (byte[] payload1, byte[] payload2) =
      LzmaTestChunkedLiteralEncoder.EncodeTwoLiteralChunks(props, plain1, plain2);

    byte[] src = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsNoResetStateThenEnd(
      props,
      payload1,
      (uint)plain1.Length,
      payload2,
      (uint)plain2.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain1.Length + plain2.Length];
    Lzma2DecodeResult res = dec.Decode(src, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(src.Length, consumed);
    Assert.Equal(dst.Length, written);

    byte[] expected = new byte[dst.Length];
    plain1.CopyTo(expected, 0);
    plain2.CopyTo(expected, plain1.Length);
    Assert.Equal(expected, dst);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] plain1 = [(byte)'A', (byte)'B', (byte)'C', (byte)'D', (byte)'E'];
    byte[] plain2 = [(byte)'F', (byte)'G', (byte)'H', (byte)'I'];

    (byte[] payload1, byte[] payload2) =
      LzmaTestChunkedLiteralEncoder.EncodeTwoLiteralChunks(props, plain1, plain2);

    byte[] src = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsNoResetStateThenEnd(
      props,
      payload1,
      (uint)plain1.Length,
      payload2,
      (uint)plain2.Length);

    byte[] expected = new byte[plain1.Length + plain2.Length];
    plain1.CopyTo(expected, 0);
    plain2.CopyTo(expected, plain1.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[expected.Length];

    int srcOffset = 0;
    int dstOffset = 0;

    while (true)
    {
      ReadOnlySpan<byte> srcChunk = src.AsSpan(srcOffset, Math.Min(2, src.Length - srcOffset));
      Span<byte> dstChunk = dst.AsSpan(dstOffset, Math.Min(1, dst.Length - dstOffset));

      Lzma2DecodeResult res = dec.Decode(srcChunk, dstChunk, out int consumed, out int written);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      srcOffset += consumed;
      dstOffset += written;

      if (res == Lzma2DecodeResult.Finished)
      {
        Assert.Equal(src.Length, srcOffset);
        Assert.Equal(dst.Length, dstOffset);
        break;
      }

      if (srcOffset == src.Length && dstOffset == dst.Length)
        throw new InvalidOperationException("Декодер не завершился, хотя ввод закончился и место под вывод кончилось.");
    }

    Assert.Equal(expected, dst);
  }
}
