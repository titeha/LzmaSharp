using System.Text;

using LzmaCore.SevenZip;

namespace LzmaCore.Tests;

public class Lzma2DecTests
{
  [Fact]
  public void Lzma2_UncompressedChunk_DecodesHello_AndFinishes()
  {
    // LZMA2 prop: 16 => словарь ~ 1 MiB (для несжатого чанка не критично, но пусть будет валидно).
    byte prop = 16;

    CLzma2Dec dec = default;
    Assert.Equal(Sz.OK, Lzma2Dec.AllocateProbs(ref dec, prop));

    dec.Decoder.Dic = new byte[64];
    dec.Decoder.DicBufSize = dec.Decoder.Dic.Length;
    dec.Decoder.DicPos = 0;

    Lzma2Dec.Init(ref dec);

    // LZMA2: [0x01 reset-dic+copy] [unpackSize-1 hi] [lo] [данные...] [0x00 end]
    // unpackSize = 5 => unpackSize-1 = 4 => 0x0004
    byte[] src = { 0x01, 0x00, 0x04, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0x00 };

    int srcLen = src.Length;
    int res = Lzma2Dec.DecodeToDic(ref dec, dicLimit: 64, src, ref srcLen, LzmaFinishMode.End, out var status);

    Assert.Equal(Sz.OK, res);
    Assert.Equal(LzmaStatus.FinishedWithMark, status);
    Assert.Equal(5, dec.Decoder.DicPos);

    string s = Encoding.ASCII.GetString(dec!.Decoder.Dic!, 0, dec.Decoder.DicPos);
    Assert.Equal("hello", s);
  }
}
