using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDecoderRep2Tests
{
  [Fact]
  public void Decode_Rep2_Copies_Previous_Byte()
  {
    // Стандартный байт properties, который часто встречается в .lzma (lc=3, lp=0, pb=2)
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    // Поток: init + 'A' + rep2 длиной 3 (в сумме получаем 4 байта: 'A' + 3 повтора)
    byte[] src = LzmaTestRep0Encoder.Encode_OneLiteral_Then_Rep2(props, repLen: 3, literal: (byte)'A');

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 16);

    Span<byte> dst = stackalloc byte[4];
    var res = decoder.Decode(src, out int consumed, dst, out int written, out LzmaProgress progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);

    // Важно: наш декодер на этом этапе не знает "unpackSize" и останавливается,
    // когда заполняет dst. При этом тестовый range-энкодер в конце делает Flush()
    // (для формирования корректного range-потока) и может добавить хвостовые байты,
    // которые декодеру уже не нужны для получения этих 4 байт вывода.
    // Поэтому consumed может быть < src.Length — это нормально.
    Assert.InRange(consumed, 5, src.Length);
    Assert.Equal(dst.Length, written);

    Assert.Equal("AAAA"u8.ToArray(), dst.ToArray());

    Assert.Equal((long)consumed, progress.BytesRead);
    Assert.Equal((long)dst.Length, progress.BytesWritten);
  }
}
