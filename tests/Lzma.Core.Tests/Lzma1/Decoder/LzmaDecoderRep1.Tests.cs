using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDecoderRep1Tests
{
  [Fact]
  public void Decode_Rep1_Copies_Previous_Byte()
  {
    // Берём стандартные свойства, как в остальных тестах.
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    // Поток: 'A' (литерал), затем rep1 (дистанция rep1, в начале она равна 1)
    // и длина 3. Итого ожидаем 1 + 3 = 4 байта 'A'.
    byte[] lzma = LzmaTestRep0Encoder.Encode_OneLiteral_Then_Rep1(props, repLen: 3, literal: (byte)'A');

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    byte[] dst = new byte[4];
    var res = dec.Decode(lzma, out int consumed, dst, out int written, out var progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(dst.Length, written);

    byte[] expected = "AAAA"u8.ToArray();
    Assert.Equal(expected, dst);

    // Бонус: прогресс должен быть согласован с фактом записи.
    Assert.Equal(written, progress.BytesWritten);
    Assert.Equal(consumed, progress.BytesRead);
  }
}
