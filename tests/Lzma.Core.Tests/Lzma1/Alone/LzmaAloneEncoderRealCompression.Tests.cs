using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneEncoderRealCompressionTests
{
  private const int DictionarySize = 1 << 16;

  private static LzmaProperties Props => new(3, 0, 2);

  [Theory]
  [InlineData("")]
  [InlineData("A")]
  [InlineData("AB")]
  [InlineData("AAAAA")]
  [InlineData("ABCABCABCABC")]
  [InlineData("the quick brown fox jumps over the lazy dog")]
  public void Encode_RoundTrip_ДляТекста(string text)
  {
    byte[] input = Encoding.UTF8.GetBytes(text);

    AssertRoundTrip(input);
  }

  [Fact]
  public void Encode_RoundTrip_ДляПовторяющихсяНулей()
  {
    AssertRoundTrip(new byte[1000]);
  }

  [Fact]
  public void Encode_RoundTrip_ДляПовторяющегосяПаттерна()
  {
    byte[] input = MakeRepeated("Lorem ipsum dolor sit amet. ", 100);

    AssertRoundTrip(input);
  }

  [Fact]
  public void Encode_RoundTrip_ДляСлучайныхДанных()
  {
    var random = new Random(20260618);
    byte[] input = new byte[8192];
    random.NextBytes(input);

    AssertRoundTrip(input);
  }

  [Fact]
  public void Encode_ПовторяющиесяДанные_СжимаютсяМеньшеОригинала()
  {
    byte[] input = new byte[2000];
    Array.Fill(input, (byte)'X');

    byte[] encoded = LzmaAloneEncoder.Encode(input, Props, DictionarySize);

    Assert.True(
        encoded.Length < input.Length,
        $"Ожидалось сжатие: encoded={encoded.Length}, input={input.Length}.");
  }

  [Fact]
  public void Encode_ПаттернныйТекст_СжимаетсяМеньшеОригинала()
  {
    byte[] input = MakeRepeated("the quick brown fox. ", 200);

    byte[] encoded = LzmaAloneEncoder.Encode(input, Props, DictionarySize);

    Assert.True(
        encoded.Length < input.Length,
        $"Ожидалось сжатие: encoded={encoded.Length}, input={input.Length}.");
  }

  private static void AssertRoundTrip(byte[] input)
  {
    byte[] encoded = LzmaAloneEncoder.Encode(input, Props, DictionarySize);

    byte[] decoded = DecodeAlone(encoded, input.Length);

    Assert.Equal(input, decoded);
  }

  private static byte[] DecodeAlone(byte[] encoded, int expectedLength)
  {
    var decoder = new LzmaAloneIncrementalDecoder();
    byte[] output = new byte[expectedLength];

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      LzmaAloneDecodeResult result = decoder.Decode(
          encoded.AsSpan(inPos),
          output.AsSpan(outPos),
          out int consumed,
          out int written);

      inPos += consumed;
      outPos += written;

      if (result == LzmaAloneDecodeResult.Finished)
        return output;

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
