using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneEncoderTests
{
  [Fact]
  public void EncodeLiteralOnly_ФормируетКорректныйЗаголовокИPayload_ИДекодитсяНазад()
  {
    // Входные данные (несколько байт, включая 0 и 255 — чтобы поймать странные случаи).
    byte[] input = [65, 66, 67, 0, 255];

    // Небольшой словарь — нам достаточно для теста.
    const int dictionarySize = 1 << 16;

    // Выберем «обычные» properties.
    var props = new LzmaProperties(3, 0, 2);

    byte[] encoded = LzmaAloneEncoder.EncodeLiteralOnly(input, props, dictionarySize);

    // 1) Проверяем, что заголовок читается обратно и соответствует ожиданиям.
    var headerRead = LzmaAloneHeader.TryRead(encoded, out var header, out int headerBytesRead);
    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, headerRead);
    Assert.Equal(LzmaAloneHeader.HeaderSize, headerBytesRead);
    Assert.Equal(props, header.Properties);
    Assert.Equal(dictionarySize, header.DictionarySize);
    Assert.True(header.UncompressedSize.HasValue);
    Assert.Equal((ulong)input.Length, header.UncompressedSize!.Value);

    // 2) Пробуем декодировать весь поток одним вызовом.
    var decoder = new LzmaAloneIncrementalDecoder();

    byte[] output = new byte[input.Length];

    var res = decoder.Decode(encoded, output, out int consumed, out int written);
    Assert.Equal(LzmaAloneDecodeResult.Finished, res);
    Assert.Equal(encoded.Length, consumed);
    Assert.Equal(input.Length, written);

    Assert.Equal(input, output);
  }

  [Fact]
  public void EncodeLiteralOnly_РаботаетПриПотоковойПодаче_КрошечнымиКусками()
  {
    // Сделаем вход длиннее, чтобы проверить «стриминг».
    byte[] input = new byte[200];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)i;

    const int dictionarySize = 1 << 16;
    var props = new LzmaProperties(3, 0, 2);

    byte[] encoded = LzmaAloneEncoder.EncodeLiteralOnly(input, props, dictionarySize);

    var decoder = new LzmaAloneIncrementalDecoder();

    byte[] output = new byte[input.Length];

    DecodeAllStreamed(
      decoder: decoder,
      encoded: encoded,
      output: output,
      maxInChunk: 3,
      maxOutChunk: 5);

    Assert.Equal(input, output);
  }

  private static void DecodeAllStreamed(
    LzmaAloneIncrementalDecoder decoder,
    ReadOnlySpan<byte> encoded,
    Span<byte> output,
    int maxInChunk,
    int maxOutChunk)
  {
    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      if (outPos == output.Length)
        return;

      int inLen = Math.Min(maxInChunk, encoded.Length - inPos);
      int outLen = Math.Min(maxOutChunk, output.Length - outPos);

      // Если вход кончился, а выход ещё нет — это ошибка теста.
      if (inLen == 0)
        throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");

      var res = decoder.Decode(
        input: encoded.Slice(inPos, inLen),
        output: output.Slice(outPos, outLen),
        bytesConsumed: out int consumed,
        bytesWritten: out int written);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == LzmaAloneDecodeResult.Finished)
        return;

      // На этом шаге ожидаем только эти промежуточные состояния.
      Assert.True(
        res == LzmaAloneDecodeResult.NeedMoreInput ||
        res == LzmaAloneDecodeResult.NeedMoreOutput,
        $"Неожиданный результат: {res}");
    }
  }
}
