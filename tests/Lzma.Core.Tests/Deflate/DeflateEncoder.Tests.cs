using System.IO.Compression;
using System.Text;

using Lzma.Core.Deflate;

namespace Lzma.Core.Tests.Deflate;

public sealed class DeflateEncoderTests
{
  [Theory]
  [InlineData("")]
  [InlineData("A")]
  [InlineData("Hello, DEFLATE encoder!")]
  [InlineData("ABCABCABCABCABCABCABCABCABCABCABCABCABCABC")]
  [InlineData("the quick brown fox jumps over the lazy dog the quick brown fox")]
  public void Encode_RoundTripИCrossCheck_ДляТекста(string text)
  {
    AssertEncode(Encoding.UTF8.GetBytes(text));
  }

  [Fact]
  public void Encode_Нули()
  {
    AssertEncode(new byte[50_000]);
  }

  [Fact]
  public void Encode_ПовторяющийсяПаттерн_Сжимается()
  {
    byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 500)));

    byte[] encoded = AssertEncode(input);

    Assert.True(encoded.Length < input.Length, $"Ожидалось сжатие: {encoded.Length} < {input.Length}.");
  }

  [Fact]
  public void Encode_СлучайныеДанные_НеПадаетИВалиден()
  {
    var random = new Random(20260619);
    byte[] input = new byte[40_000];
    random.NextBytes(input);

    AssertEncode(input);
  }

  [Fact]
  public void Encode_БольшойТекст_БольшеОкна()
  {
    byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("structured repeating text block ", 5000)));

    AssertEncode(input);
  }

  [Fact]
  public void Encode_НесжимаемыеДанные_ВыбираетStored_БезРаздувания()
  {
    var random = new Random(123);
    byte[] input = new byte[20_000];
    random.NextBytes(input);

    byte[] encoded = DeflateEncoder.Encode(input);

    // Для несжимаемых данных stored-fallback не должен раздувать больше, чем на ~накладные.
    Assert.True(encoded.Length <= input.Length + 64, $"Слишком большое раздувание: {encoded.Length} vs {input.Length}.");
  }

  /// <summary>
  /// Кодирует, затем проверяет двумя независимыми декодерами: нашим и BCL.
  /// </summary>
  private static byte[] AssertEncode(byte[] input)
  {
    byte[] encoded = DeflateEncoder.Encode(input);

    // 1) Наш декодер.
    byte[] ours = new byte[input.Length];
    DeflateDecodeResult result = DeflateDecoder.Decode(encoded, ours, out _, out int written);
    Assert.Equal(DeflateDecodeResult.Ok, result);
    Assert.Equal(input.Length, written);
    Assert.Equal(input, ours);

    // 2) BCL DeflateStream (независимый проверенный декодер).
    Assert.Equal(input, BclDecompress(encoded, input.Length));

    return encoded;
  }

  private static byte[] BclDecompress(byte[] deflate, int expectedLength)
  {
    using var ms = new MemoryStream(deflate);
    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
    using var outMs = new MemoryStream(expectedLength);
    ds.CopyTo(outMs);
    return outMs.ToArray();
  }
}
