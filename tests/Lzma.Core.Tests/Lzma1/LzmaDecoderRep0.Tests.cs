using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDecoderRep0Tests
{
  [Fact]
  public void Decode_ShortRep0_Copies_Previous_Byte()
  {
    // Эти параметры совпадают с другими тестами.
    // Важно: Pb = 2 -> numPosStates = 4.
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    const int dictionarySize = 1 << 16;

    // Поток: 'A' (литерал) + short rep0 (длина = 1, dist = rep0).
    // rep0 по умолчанию равен 1, то есть мы копируем предыдущий байт.
    var encoder = new LzmaTestRep0Encoder(props);
    byte[] input = encoder.Encode_OneLiteral_Then_ShortRep0((byte)'A');

    var decoder = new LzmaDecoder(props, dictionarySize);

    Span<byte> output = stackalloc byte[2];
    var res = decoder.Decode(input, out int bytesConsumed, output, out int bytesWritten, out var progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(2, bytesWritten);
    Assert.Equal(new byte[] { (byte)'A', (byte)'A' }, output.ToArray());

    // Прогресс должен совпадать с фактом записи.
    Assert.Equal(bytesConsumed, progress.BytesRead);
    Assert.Equal(2, progress.BytesWritten);
  }

  [Fact]
  public void Decode_LongRep0_Copies_From_Dictionary_With_Overlap()
  {
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    const int dictionarySize = 1 << 16;

    // Поток: 'Q' (литерал) + long rep0 (длина = 6).
    // Ожидаемый результат: 1 + 6 = 7 байт 'Q'.
    var encoder = new LzmaTestRep0Encoder(props);
    byte[] input = encoder.Encode_OneLiteral_Then_LongRep0((byte)'Q', repLen: 6);

    var decoder = new LzmaDecoder(props, dictionarySize);

    byte[] output = new byte[7];
    var res = decoder.Decode(input, out _, output, out int bytesWritten, out _);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(output.Length, bytesWritten);
    for (int i = 0; i < output.Length; i++)
    {
      Assert.Equal((byte)'Q', output[i]);
    }
  }
}
