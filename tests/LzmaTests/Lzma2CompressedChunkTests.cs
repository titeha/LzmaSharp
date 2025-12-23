using System.Text;

using LzmaCore.SevenZip;

namespace LzmaCore.Tests;

public class Lzma2CompressedChunkTests
{
  [Fact]
  public void Lzma2_СжатыйЧанк_РаспаковываетсяВТочныйОригинал()
  {
    byte[] original = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog 1234567890.");

    // 24.09 SDK энкодер -> .lzma (LZMA-Alone)
    byte[] lzmaAlone = UpstreamLzmaSdk.EncodeLzmaAlone(original, dictionary: 1 << 20);

    // Заворачиваем payload в один LZMA2-чанк + end marker.
    byte[] lzma2Stream = UpstreamLzmaSdk.WrapLzmaAlonePayloadIntoLzma2(lzmaAlone, unpackSize: original.Length);

    // Свой декодер (LZMA2 prop = 16 => ~1 MiB словарь)
    const byte lzma2Prop = 16;

    CLzma2Dec dec = default;
    Assert.Equal(Sz.OK, Lzma2Dec.AllocateProbs(ref dec, lzma2Prop));

    // Словарь/выход: дадим запас 1 MiB, чтобы точно не упереться в "маленький словарь".
    dec.Decoder.Dic = new byte[Math.Max(original.Length, 1 << 20)];
    dec.Decoder.DicBufSize = dec.Decoder.Dic.Length;
    dec.Decoder.DicPos = 0;

    Lzma2Dec.Init(ref dec);

    int srcLen = lzma2Stream.Length;
    int res = Lzma2Dec.DecodeToDic(ref dec, dicLimit: original.Length, lzma2Stream, ref srcLen, LzmaFinishMode.End, out var status);

    Assert.Equal(Sz.OK, res);
    Assert.Equal(LzmaStatus.FinishedWithMark, status);
    Assert.Equal(original.Length, dec.Decoder.DicPos);

    byte[] decoded = new byte[original.Length];
    Buffer.BlockCopy(dec.Decoder.Dic!, 0, decoded, 0, original.Length);

    Assert.Equal(original, decoded);
  }
}
