using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderLzmaChunkNoPropsTests
{
  [Fact]
  public void Decode_OneShot_TwoLzmaChunks_SecondWithoutProps_ResetState()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] plain1 = Encoding.ASCII.GetBytes("First LZMA2 chunk.");
    byte[] plain2 = Encoding.ASCII.GetBytes("Second chunk (no props).");

    byte[] payload1 = LzmaTestLiteralOnlyEncoder.Encode(props, plain1);
    byte[] payload2 = LzmaTestLiteralOnlyEncoder.Encode(props, plain2);

    byte[] lzma2Stream = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsResetStateThenEnd(
      props,
      firstLzmaPayload: payload1,
      firstUnpackSize: plain1.Length,
      secondLzmaPayload: payload2,
      secondUnpackSize: plain2.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain1.Length + plain2.Length];
    var res = dec.Decode(lzma2Stream, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(lzma2Stream.Length, consumed);
    Assert.Equal(dst.Length, written);

    var expected = new byte[plain1.Length + plain2.Length];
    Buffer.BlockCopy(plain1, 0, expected, 0, plain1.Length);
    Buffer.BlockCopy(plain2, 0, expected, plain1.Length, plain2.Length);

    Assert.Equal(expected, dst);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] plain1 = Encoding.ASCII.GetBytes("Tiny chunk test: part 1.");
    byte[] plain2 = Encoding.ASCII.GetBytes("Part 2 (no props).");

    byte[] payload1 = LzmaTestLiteralOnlyEncoder.Encode(props, plain1);
    byte[] payload2 = LzmaTestLiteralOnlyEncoder.Encode(props, plain2);

    byte[] lzma2Stream = Lzma2TestStreamBuilder.TwoLzmaChunks_SecondNoPropsResetStateThenEnd(
      props,
      firstLzmaPayload: payload1,
      firstUnpackSize: plain1.Length,
      secondLzmaPayload: payload2,
      secondUnpackSize: plain2.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    var expected = new byte[plain1.Length + plain2.Length];
    Buffer.BlockCopy(plain1, 0, expected, 0, plain1.Length);
    Buffer.BlockCopy(plain2, 0, expected, plain1.Length, plain2.Length);

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

      if (res == Lzma2DecodeResult.InvalidData)
        throw new InvalidOperationException("Декодер вернул InvalidData, но тестовые данные должны быть валидными.");

      if (res == Lzma2DecodeResult.NotSupported)
        throw new InvalidOperationException("Декодер вернул NotSupported, но тест использует минимально поддерживаемые LZMA2-чанки.");

      // Если ввод закончился, а мы всё ещё не Finished — это ошибка.
      if (srcPos == lzma2Stream.Length && res == Lzma2DecodeResult.NeedMoreInput)
        throw new InvalidOperationException("Ввод закончился раньше, чем декодер завершился.");

      // Если вывод закончился, а мы всё ещё не Finished — это ошибка.
      if (dstPos == dst.Length && res == Lzma2DecodeResult.NeedMoreOutput)
        throw new InvalidOperationException("Вывод закончился, но декодер всё ещё просит NeedMoreOutput.");
    }

    Assert.Equal(lzma2Stream.Length, srcPos);
    Assert.Equal(expected.Length, dstPos);
    Assert.Equal(expected, dst);
  }
}
