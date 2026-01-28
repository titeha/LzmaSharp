using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2LzmaEncoderTests
{
  [Fact]
  public void EncodeDecode_LiteralOnly_НесколькоБайт_ДаетТочныйРезультат()
  {
    var input = new byte[] { 65, 66, 67, 0, 255 };
    int dictionarySize = 1 << 16;

    // Типичные (lc/lp/pb) для LZMA: lc=3, lp=0, pb=2.
    var lzma1Props = new LzmaProperties(3, 0, 2);

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnly(input, lzma1Props, dictionarySize, out byte lzma2PropsByte);

    // Декодер получает LZMA2-properties отдельно (как это бывает в контейнерах).
    var decoder = new Lzma2IncrementalDecoder(lzma2PropsByte);

    byte[] actual = DecodeAllStreamed(decoder, encoded, expectedOutputSize: input.Length);

    Assert.Equal(input, actual);
  }

  [Fact]
  public void EncodeDecode_LiteralOnly_РаботаетПриПотоковойПодаче_КрошечнымиКусками()
  {
    // Чуть побольше данных, чтобы гарантированно пройти разные ветки по NeedMoreInput/NeedMoreOutput.
    var input = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"u8.ToArray();
    int dictionarySize = 1 << 16;
    var lzma1Props = new LzmaProperties(3, 0, 2);

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnly(input, lzma1Props, dictionarySize, out byte lzma2PropsByte);
    var decoder = new Lzma2IncrementalDecoder(lzma2PropsByte);

    byte[] actual = DecodeAllStreamed(decoder, encoded, expectedOutputSize: input.Length, maxInChunk: 2, maxOutChunk: 3);

    Assert.Equal(input, actual);
  }

  private static byte[] DecodeAllStreamed(
    Lzma2IncrementalDecoder decoder,
    ReadOnlySpan<byte> encoded,
    int expectedOutputSize,
    int maxInChunk = 3,
    int maxOutChunk = 7)
  {
    var output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    // Декодируем до тех пор, пока декодер не сообщит об окончании потока.
    // Важно: конец LZMA2-потока — это отдельный управляющий байт (0x00).
    // Он может приехать ПОСЛЕ того, как мы уже получили весь ожидаемый распакованный вывод.
    // Поэтому:
    //  - пока outPos < output.Length мы даём декодеру место для вывода;
    //  - когда outPos == output.Length, даём пустой выходной буфер и продолжаем кормить вход,
    //    чтобы декодер смог дочитать финальный 0x00 и вернуть Finished.
    while (true)
    {
      ReadOnlySpan<byte> inChunk = encoded.Slice(inPos, Math.Min(maxInChunk, encoded.Length - inPos));

      Span<byte> outChunk = outPos < output.Length
        ? output.AsSpan(outPos, Math.Min(maxOutChunk, output.Length - outPos))
        : [];

      Lzma2DecodeResult res = decoder.Decode(inChunk, outChunk, out int consumed, out int written);

      // Защита от вечного цикла: декодер обязан либо потребить вход, либо записать выход.
      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == Lzma2DecodeResult.Finished)
        break;

      if (res == Lzma2DecodeResult.NeedMoreInput)
      {
        if (inPos >= encoded.Length)
          throw new InvalidOperationException("Декодер запросил ещё вход, но входные данные закончились.");

        continue;
      }

      if (res == Lzma2DecodeResult.NeedMoreOutput)
      {
        if (outPos >= output.Length)
          throw new InvalidOperationException("Декодер запросил ещё выход, но ожидаемый вывод уже полностью получен.");

        continue;
      }

      throw new InvalidOperationException($"Неожиданный результат декодера: {res}");
    }

    Assert.Equal(output.Length, outPos);
    Assert.Equal(encoded.Length, inPos);

    return output;
  }
}
