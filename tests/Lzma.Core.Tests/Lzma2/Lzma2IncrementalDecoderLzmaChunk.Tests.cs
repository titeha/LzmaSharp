using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderLzmaChunkTests
{
  [Fact]
  public void Decode_OneShot_LzmaChunk_Then_EndMarker()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] plain = Encoding.ASCII.GetBytes("Hello, LZMA2!");

    byte[] lzmaPayload = LzmaTestLiteralOnlyEncoder.Encode(props, plain);
    byte[] lzma2Stream = Lzma2TestStreamBuilder.SingleLzmaChunkThenEnd(
      props,
      lzmaPayload,
      unpackSize: plain.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain.Length];
    var res = dec.Decode(lzma2Stream, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(lzma2Stream.Length, consumed);
    Assert.Equal(plain.Length, written);
    Assert.Equal(plain, dst);
  }

  [Fact]
  public void Decode_OneShot_LzmaChunk_NewProps_NoDicReset_Then_EndMarker()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] plain = Encoding.ASCII.GetBytes("Hello, new-props LZMA2!");

    byte[] lzmaPayload = LzmaTestLiteralOnlyEncoder.Encode(props, plain);
    byte[] lzma2Stream = Lzma2TestStreamBuilder.SingleLzmaChunkWithNewPropsNoResetDictionaryThenEnd(
      props,
      lzmaPayload,
      unpackSize: plain.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain.Length];
    var res = dec.Decode(lzma2Stream, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(lzma2Stream.Length, consumed);
    Assert.Equal(plain.Length, written);
    Assert.Equal(plain, dst);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] plain = Encoding.ASCII.GetBytes("Tiny chunk streaming test for LZMA2.");

    byte[] lzmaPayload = LzmaTestLiteralOnlyEncoder.Encode(props, plain);
    byte[] lzma2Stream = Lzma2TestStreamBuilder.SingleLzmaChunkThenEnd(
      props,
      lzmaPayload,
      unpackSize: plain.Length);

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);

    byte[] dst = new byte[plain.Length];

    int srcPos = 0;
    int dstPos = 0;

    int guard = 0;
    while (true)
    {
      if (++guard > 100_000)
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
        throw new InvalidOperationException("Декодер вернул NotSupported, но тест использует минимально поддерживаемый LZMA-чанк.");

      // Если ввод закончился, а мы всё ещё не Finished — это ошибка.
      if (srcPos == lzma2Stream.Length && res == Lzma2DecodeResult.NeedMoreInput)
        throw new InvalidOperationException("Ввод закончился раньше, чем декодер завершился.");

      // Если вывод закончился, а мы всё ещё не Finished — это ошибка.
      if (dstPos == dst.Length && res == Lzma2DecodeResult.NeedMoreOutput)
      {
        // В этом тесте размер ожидаемого выхода фиксированный и равен plain.Length.
        // Если мы уже вывели всё, но декодер всё ещё просит выход — это баг.
        throw new InvalidOperationException("Вывод закончился, но декодер всё ещё просит NeedMoreOutput.");
      }
    }

    Assert.Equal(lzma2Stream.Length, srcPos);
    Assert.Equal(plain.Length, dstPos);
    Assert.Equal(plain, dst);
  }
}
