using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaEncoderMatchTests
{
  [Fact]
  public void EncodeDecode_ОбычныйMatch_Distance1_ПовторяетБайт()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    var encoder = new LzmaEncoder(props);

    var ops = new[]
    {
      LzmaEncodeOp.Lit((byte)'A'),
      // Один literal + match с расстоянием 1 -> получаем 5 байт 'A'
      LzmaEncodeOp.Match(distance: 1, length: 4),
    };

    byte[] encoded = encoder.EncodeScript(ops);

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 20);
    byte[] decoded = new byte[5];

    var result = decoder.Decode(encoded, out int consumed, decoded, out int written, out _);

    Assert.Equal(LzmaDecodeResult.Ok, result);
    Assert.True(consumed > 0);
    Assert.Equal(decoded.Length, written);

    Assert.Equal(Encoding.ASCII.GetBytes("AAAAA"), decoded);
  }

  [Fact]
  public void Decode_РаботаетПриПотоковойПодаче_ПослеMatchИдётMatchedLiteral()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    var encoder = new LzmaEncoder(props);

    var ops = new[]
    {
      LzmaEncodeOp.Lit((byte)'A'),
      LzmaEncodeOp.Lit((byte)'B'),
      LzmaEncodeOp.Lit((byte)'C'),
      // Match: копируем "ABC" ещё раз (distance=3, len=3)
      LzmaEncodeOp.Match(distance: 3, length: 3),
      // После match состояние не-литеральное => literal кодируется как matched literal
      LzmaEncodeOp.Lit((byte)'D'),
    };

    byte[] encoded = encoder.EncodeScript(ops);
    byte[] expected = Encoding.ASCII.GetBytes("ABCABCD");

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 20);

    int inputOffset = 0;
    int outputOffset = 0;
    var output = new byte[expected.Length];

    const int inChunkSize = 1;
    const int outChunkSize = 1;

    while (outputOffset < expected.Length)
    {
      var input = encoded.AsSpan(inputOffset, Math.Min(inChunkSize, encoded.Length - inputOffset));
      var dst = output.AsSpan(outputOffset, Math.Min(outChunkSize, expected.Length - outputOffset));

      var result = decoder.Decode(input, out int consumed, dst, out int written, out _);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inputOffset += consumed;
      outputOffset += written;

      switch (result)
      {
        case LzmaDecodeResult.Ok:
          // ОК, продолжаем пока не соберём весь ожидаемый вывод.
          break;
        case LzmaDecodeResult.NeedsMoreInput:
          if (inputOffset >= encoded.Length)
            throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");
          break;
        default:
          throw new InvalidOperationException($"Неожиданный результат декодирования: {result}.");
      }
    }

    Assert.Equal(expected, output);
  }
}
