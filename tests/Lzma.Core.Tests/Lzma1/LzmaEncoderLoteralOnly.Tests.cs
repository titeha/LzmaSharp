using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaEncoderLiteralOnlyTests
{
  [Fact]
  public void EncodeDecode_LiteralOnly_OneShot_Produces_Exact_Output()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    // Набор байт специально «разношерстный»: ASCII, 0, 255.
    byte[] original = { 65, 66, 67, 0, 255 };

    var encoder = new LzmaEncoder(props);
    byte[] encoded = encoder.EncodeLiteralOnly(original);

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 20);
    byte[] decoded = new byte[original.Length];

    var res = decoder.Decode(
      encoded,
      out int bytesConsumed,
      decoded,
      out int bytesWritten,
      out _);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(original.Length, bytesWritten);
    Assert.True(bytesConsumed > 0);

    Assert.Equal(original, decoded);
  }

  [Fact]
  public void EncodeDecode_LiteralOnly_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte[] original = "Hello, LZMA!"u8.ToArray();

    var encoder = new LzmaEncoder(props);
    byte[] encoded = encoder.EncodeLiteralOnly(original);

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 20);

    byte[] decoded = new byte[original.Length];

    int inPos = 0;
    int outPos = 0;

    while (outPos < decoded.Length)
    {
      int inChunk = Math.Min(2, encoded.Length - inPos);
      if (inChunk <= 0)
        throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");

      int outChunk = Math.Min(3, decoded.Length - outPos);

      var res = decoder.Decode(
        encoded.AsSpan(inPos, inChunk),
        out int bytesConsumed,
        decoded.AsSpan(outPos, outChunk),
        out int bytesWritten,
        out _);

      // Продвинулись либо по входу, либо по выходу.
      if (bytesConsumed == 0 && bytesWritten == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += bytesConsumed;
      outPos += bytesWritten;

      if (res == LzmaDecodeResult.NeedsMoreInput && inPos == encoded.Length)
        throw new InvalidOperationException("Вход закончился, но декодер просит ещё данные.");

      Assert.NotEqual(LzmaDecodeResult.InvalidData, res);
    }

    Assert.Equal(original, decoded);
  }
}
