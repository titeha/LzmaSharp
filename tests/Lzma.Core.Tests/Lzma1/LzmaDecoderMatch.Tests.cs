using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDecoderMatchTests
{
  [Fact]
  public void Decode_OneShot_Can_Decode_Simple_Match_Distance1()
  {
    // lc=3, lp=0, pb=2 => 93
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    const int dictionarySize = 1 << 16;

    // 6 байт 'A': первый литерал + один match(distance=1, len=5)
    byte[] expected = System.Text.Encoding.ASCII.GetBytes("AAAAAA");
    byte[] compressed = LzmaTestSimpleMatchEncoder.Encode_A_Run_With_One_Match(props, expected.Length);

    var decoder = new LzmaDecoder(props, dictionarySize);
    byte[] output = new byte[expected.Length];

    LzmaDecodeResult res = decoder.Decode(compressed, out int consumed, output, out int written, out _);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(expected.Length, written);
    Assert.True(consumed > 0);
    Assert.Equal(expected, output);
  }
}
