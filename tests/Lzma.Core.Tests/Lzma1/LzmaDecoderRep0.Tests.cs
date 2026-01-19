using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

/// <summary>
/// <para>Тесты на rep0 (самая первая и частая «повторная дистанция» в LZMA).</para>
/// <para>Мы пока не покрываем rep1/rep2/rep3 — это будут следующие маленькие шаги.</para>
/// </summary>
public sealed class LzmaDecoderRep0Tests
{
  [Fact]
  public void Decode_ShortRep0_Copies_Previous_Byte()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte a = (byte)'A';
    byte[] lzma = LzmaTestRep0Encoder.Encode_OneLiteral_Then_ShortRep0(props, a);

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    Span<byte> dst = stackalloc byte[2];
    var res = dec.Decode(lzma, out _, dst, out int written, out _);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(2, written);
    Assert.Equal(a, dst[0]);
    Assert.Equal(a, dst[1]);
  }

  [Fact]
  public void Decode_LongRep0_Copies_From_Dictionary_With_Overlap()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte a = (byte)'A';
    const int repLen = 3; // output: 1 + 3 = 4 байта

    byte[] lzma = LzmaTestRep0Encoder.Encode_OneLiteral_Then_LongRep0(props, a, repLen);

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    Span<byte> dst = stackalloc byte[4];
    var res = dec.Decode(lzma, out _, dst, out int written, out _);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(4, written);

    for (int i = 0; i < written; i++)
      Assert.Equal(a, dst[i]);
  }
}
