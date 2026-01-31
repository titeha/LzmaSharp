using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public class Lzma2DecoderTests
{
  [Fact]
  public void DecodeToArray_Декодирует_CopyПоток_ВТочныйРезультат()
  {
    byte[] data = [1, 2, 3, 4, 5, 0, 255, 100, 101, 102];
    const int dictionarySize = 1 << 20;

    byte[] encoded = Lzma2CopyEncoder.Encode(data, dictionarySize, out byte dictProp);

    Assert.True(Lzma2Properties.TryParse(dictProp, out var props));
    Assert.True(props.TryGetDictionarySizeInt32(out int dictFromProps));

    Lzma2DecodeResult result = Lzma2Decoder.DecodeToArray(
        encoded,
        dictFromProps,
        out byte[] decoded,
        out int bytesConsumed);

    Assert.Equal(Lzma2DecodeResult.Finished, result);
    Assert.Equal(encoded.Length, bytesConsumed);
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void DecodeToArray_Декодирует_LzmaПоток_ВТочныйРезультат()
  {
    byte[] data = [(byte)'A', (byte)'B', (byte)'C', 0, 255, (byte)'D', (byte)'E', (byte)'F', (byte)'G'];
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var lzmaProps));

    int dictionarySize = 1 << 20;
    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunkedAuto(
        data,
        lzmaProps,
        dictionarySize,
        1 << 16,
        out _);

    Lzma2DecodeResult result = Lzma2Decoder.DecodeToArray(
        encoded,
        dictionarySize,
        out byte[] decoded,
        out int bytesConsumed);

    Assert.Equal(Lzma2DecodeResult.Finished, result);
    Assert.Equal(encoded.Length, bytesConsumed);
    Assert.Equal(data, decoded);
  }
}
