using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2LzmaEncoderRealCompressionTests
{
  private const int Dict = 1 << 16;

  private static LzmaProperties Props => new(3, 0, 2);

  [Theory]
  [InlineData("")]
  [InlineData("A")]
  [InlineData("Hello LZMA2 real compression!")]
  [InlineData("ABCABCABCABCABCABCABCABC")]
  public void Encode_RoundTrip_ДляТекста(string text)
  {
    byte[] input = Encoding.UTF8.GetBytes(text);

    AssertRoundTrip(input, Dict);
  }

  [Fact]
  public void Encode_ПустойВход_ДаётТолькоEndMarker()
  {
    byte[] encoded = Lzma2LzmaEncoder.Encode([], Props, Dict);

    Assert.Equal([0x00], encoded);
  }

  [Fact]
  public void Encode_RoundTrip_ДляНулей()
  {
    AssertRoundTrip(new byte[5000], Dict);
  }

  [Fact]
  public void Encode_RoundTrip_ДляПаттерна()
  {
    byte[] input = MakeRepeated("Lorem ipsum dolor sit amet. ", 100);

    AssertRoundTrip(input, Dict);
  }

  [Fact]
  public void Encode_RoundTrip_ДляСлучайныхДанных()
  {
    var random = new Random(20260618);
    byte[] input = new byte[8192];
    random.NextBytes(input);

    AssertRoundTrip(input, Dict);
  }

  [Fact]
  public void Encode_RoundTrip_БольшеОдногоЧанка_ПринудительноМаленькиеЧанки()
  {
    byte[] input = MakeRepeated("the quick brown fox. ", 100);

    // Маленький лимит чанка гарантирует несколько чанков.
    byte[] encoded = Lzma2LzmaEncoder.Encode(input, Props, Dict, maxUnpackChunkSize: 64);

    Assert.True(CountChunks(encoded) >= 2);

    byte[] decoded = DecodeAll(encoded, input.Length, Dict);
    Assert.Equal(input, decoded);
  }

  [Fact]
  public void Encode_RoundTrip_БольшеОдногоЧанка_ВходБольше64КБ()
  {
    // > 64 КБ при дефолтном лимите чанка => гарантированно несколько чанков.
    byte[] input = new byte[200_000];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)((i * 7) & 0xFF);

    byte[] encoded = Lzma2LzmaEncoder.Encode(input, Props, Dict);

    Assert.True(CountChunks(encoded) >= 2);

    byte[] decoded = DecodeAll(encoded, input.Length, Dict);
    Assert.Equal(input, decoded);
  }

  [Fact]
  public void Encode_Нули_СжимаютсяМеньшеОригинала()
  {
    byte[] input = new byte[10_000];

    byte[] encoded = Lzma2LzmaEncoder.Encode(input, Props, Dict);

    Assert.True(
        encoded.Length < input.Length,
        $"Ожидалось сжатие: encoded={encoded.Length}, input={input.Length}.");
  }

  private static void AssertRoundTrip(byte[] input, int dictionarySize)
  {
    byte[] encoded = Lzma2LzmaEncoder.Encode(input, Props, dictionarySize);

    byte[] decoded = DecodeAll(encoded, input.Length, dictionarySize);

    Assert.Equal(input, decoded);
  }

  private static int CountChunks(byte[] encoded)
  {
    int count = 0;
    int offset = 0;

    while (offset < encoded.Length)
    {
      Lzma2ReadHeaderResult result = Lzma2ChunkHeader.TryRead(
          encoded.AsSpan(offset),
          out Lzma2ChunkHeader header,
          out int headerBytes);

      Assert.Equal(Lzma2ReadHeaderResult.Ok, result);

      if (header.Kind == Lzma2ChunkKind.End)
        break;

      count++;
      offset += headerBytes + header.PayloadSize;
    }

    return count;
  }

  private static byte[] DecodeAll(byte[] encoded, int expectedOutputSize, int dictionarySize)
  {
    var decoder = new Lzma2IncrementalDecoder(progress: null, dictionarySize: dictionarySize);

    byte[] output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      Lzma2DecodeResult result = decoder.Decode(
          encoded.AsSpan(inPos),
          output.AsSpan(outPos),
          out int consumed,
          out int written);

      inPos += consumed;
      outPos += written;

      if (result == Lzma2DecodeResult.Finished)
        return output;

      if (result is Lzma2DecodeResult.InvalidData or Lzma2DecodeResult.NotSupported)
        throw new InvalidOperationException($"Неожиданный результат декодирования: {result}.");

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException($"Декодер не продвинулся, результат: {result}.");
    }
  }

  private static byte[] MakeRepeated(string unit, int times)
  {
    var builder = new StringBuilder(unit.Length * times);

    for (int i = 0; i < times; i++)
      builder.Append(unit);

    return Encoding.UTF8.GetBytes(builder.ToString());
  }
}
