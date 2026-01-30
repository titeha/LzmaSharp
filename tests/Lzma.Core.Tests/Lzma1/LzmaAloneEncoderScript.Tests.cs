using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneEncoderScriptTests
{
  [Fact]
  public void EncodeScript_ПишетКорректныйЗаголовок()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var props));
    const int dictionarySize = 1 << 20;

    // ABC (literal) + ABC (match distance 3, len 3) + D (literal)
    var script = new[]
    {
      LzmaEncodeOp.Lit((byte)'A'),
      LzmaEncodeOp.Lit((byte)'B'),
      LzmaEncodeOp.Lit((byte)'C'),
      LzmaEncodeOp.Match(3, 3),
      LzmaEncodeOp.Lit((byte)'D'),
    };

    byte[] encoded = LzmaAloneEncoder.EncodeScript(script, props, dictionarySize);

    var headerRead = LzmaAloneHeader.TryRead(encoded, out var header, out int headerBytes);
    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, headerRead);
    Assert.Equal(LzmaAloneHeader.HeaderSize, headerBytes);
    Assert.Equal(props, header.Properties);
    Assert.Equal(dictionarySize, header.DictionarySize);
    Assert.Equal((ulong)7, header.UncompressedSize);
  }

  [Fact]
  public void EncodeDecode_ОбычныйMatch_РаботаетЧерезLzmaAlone_ВПотоке()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var props));
    const int dictionarySize = 1 << 20;

    // Сценарий тот же, что и в LzmaEncoderMatchTests.
    // Важно: финальный Literal после Match должен кодироваться как MatchedLiteral.
    var script = new[]
    {
      LzmaEncodeOp.Lit((byte)'A'),
      LzmaEncodeOp.Lit((byte)'B'),
      LzmaEncodeOp.Lit((byte)'C'),
      LzmaEncodeOp.Match(3, 3),
      LzmaEncodeOp.Lit((byte)'D'),
    };

    byte[] encoded = LzmaAloneEncoder.EncodeScript(script, props, dictionarySize);

    var decoder = new LzmaAloneIncrementalDecoder();
    byte[] decoded = DecodeAllStreamed(decoder, encoded, expectedOutputSize: 7, maxInChunk: 1, maxOutChunk: 1);

    Assert.Equal(new byte[] { (byte)'A', (byte)'B', (byte)'C', (byte)'A', (byte)'B', (byte)'C', (byte)'D' }, decoded);
  }

  private static byte[] DecodeAllStreamed(
    LzmaAloneIncrementalDecoder decoder,
    ReadOnlySpan<byte> encoded,
    int expectedOutputSize,
    int maxInChunk,
    int maxOutChunk)
  {
    byte[] output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    while (outPos < expectedOutputSize)
    {
      int inChunk = Math.Min(maxInChunk, encoded.Length - inPos);
      int outChunk = Math.Min(maxOutChunk, expectedOutputSize - outPos);

      var res = decoder.Decode(
        encoded.Slice(inPos, inChunk),
        output.AsSpan(outPos, outChunk),
        out int consumed,
        out int written);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == LzmaAloneDecodeResult.Finished)
        break;

      Assert.True(res is LzmaAloneDecodeResult.NeedMoreInput or LzmaAloneDecodeResult.NeedMoreOutput);
    }

    Assert.Equal(expectedOutputSize, outPos);
    return output;
  }
}
