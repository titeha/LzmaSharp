using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// Регрессия на баг LZMA2 reset-state previousByte: при LZMA-чанке со сбросом состояния, но
/// БЕЗ сброса словаря (control 0xA0/0xC0), контекст первого литерала должен браться из
/// последнего байта словаря (как в эталонном 7-Zip: <c>dic[dicPos-1]</c>), а не из нуля.
/// Раньше декодер ставил previousByte=0 и рассыпался на copy-первых архивах.
/// </summary>
public sealed class Lzma2ResetStatePreviousByteRegressionTests
{
  [Fact]
  public void CopyЧанкЗатемLzmaResetState_КонтекстИзСловаря_ДекодируетсяВерно()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    // Первый чанк — несжатый (copy, reset dictionary): наполняет словарь "AB".
    byte[] copyData = Encoding.ASCII.GetBytes("AB");
    byte[] copyChunk = new byte[3 + copyData.Length];
    copyChunk[0] = 0x01; // copy + reset dictionary
    int sizeMinus1 = copyData.Length - 1;
    copyChunk[1] = (byte)(sizeMinus1 >> 8);
    copyChunk[2] = (byte)(sizeMinus1 & 0xFF);
    copyData.CopyTo(copyChunk, 3);

    // Второй чанк — LZMA с reset state + props, БЕЗ reset dictionary (control 0xC0).
    // Контекст первого литерала = последний байт словаря ('B', != 0) — на этом и ловится баг.
    byte[] lzmaData = Encoding.ASCII.GetBytes("C");
    Assert.NotEqual(0, copyData[^1]); // граничный байт ненулевой → previousByte=0 дал бы неверный результат

    byte[] payload = LzmaTestLiteralOnlyEncoder.Encode(props, lzmaData, initialPreviousByte: copyData[^1]);
    byte[] lzmaChunkThenEnd = Lzma2TestStreamBuilder.SingleLzmaChunkWithNewPropsNoResetDictionaryThenEnd(
        props, payload, lzmaData.Length);

    byte[] stream = [.. copyChunk, .. lzmaChunkThenEnd];

    var dec = new Lzma2IncrementalDecoder(dictionarySize: 1 << 20);
    byte[] dst = new byte[copyData.Length + lzmaData.Length];

    Lzma2DecodeResult res = dec.Decode(stream, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(stream.Length, consumed);
    Assert.Equal(dst.Length, written);
    Assert.Equal("ABC", Encoding.ASCII.GetString(dst));
  }
}
