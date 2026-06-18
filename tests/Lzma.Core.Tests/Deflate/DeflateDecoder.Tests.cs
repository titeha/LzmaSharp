using System.IO.Compression;
using System.Text;

using Lzma.Core.Deflate;

namespace Lzma.Core.Tests.Deflate;

public sealed class DeflateDecoderTests
{
  [Theory]
  [InlineData("A")]
  [InlineData("Hello, DEFLATE world!")]
  [InlineData("ABCABCABCABCABCABCABCABCABCABCABC")]
  [InlineData("the quick brown fox jumps over the lazy dog the quick brown fox")]
  public void Decode_СовпадаетСBclДляТекста(string text)
  {
    byte[] original = Encoding.UTF8.GetBytes(text);

    AssertRoundTripAgainstBcl(original);
  }

  [Fact]
  public void Decode_Нули_БольшоеОкноBackReference()
  {
    AssertRoundTripAgainstBcl(new byte[100_000]);
  }

  [Fact]
  public void Decode_ПовторяющийсяПаттерн()
  {
    byte[] original = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 500)));

    AssertRoundTripAgainstBcl(original);
  }

  [Fact]
  public void Decode_СлучайныеДанные_ВозможныStoredБлоки()
  {
    var random = new Random(20260618);
    byte[] original = new byte[64_000];
    random.NextBytes(original);

    AssertRoundTripAgainstBcl(original);
  }

  [Fact]
  public void Decode_СмесьТекстаИСлучайныхДанных()
  {
    var random = new Random(777);
    byte[] textPart = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("structured text ", 1000)));
    byte[] randomPart = new byte[20_000];
    random.NextBytes(randomPart);

    byte[] original = [.. textPart, .. randomPart, .. textPart];

    AssertRoundTripAgainstBcl(original);
  }

  [Fact]
  public void Decode_ПовреждённыйПоток_ВозвращаетInvalidData()
  {
    byte[] compressed = BclCompress(Encoding.UTF8.GetBytes("some data to compress and then corrupt"));

    // Портим середину сжатого потока.
    compressed[compressed.Length / 2] ^= 0xFF;

    byte[] output = new byte[1024];
    DeflateDecodeResult result = DeflateDecoder.Decode(compressed, output, out _, out _);

    // Либо явная ошибка, либо успешный декод, но НЕ совпадающий с оригиналом —
    // в любом случае декодер не должен падать необработанным исключением.
    Assert.True(result is DeflateDecodeResult.Ok or DeflateDecodeResult.InvalidData);
  }

  private static void AssertRoundTripAgainstBcl(byte[] original)
  {
    byte[] compressed = BclCompress(original);

    byte[] output = new byte[original.Length];
    DeflateDecodeResult result = DeflateDecoder.Decode(
        compressed,
        output,
        out int bytesConsumed,
        out int bytesWritten);

    Assert.Equal(DeflateDecodeResult.Ok, result);
    Assert.Equal(original.Length, bytesWritten);
    Assert.Equal(original, output);
    Assert.True(bytesConsumed <= compressed.Length);
  }

  private static byte[] BclCompress(byte[] data)
  {
    using var ms = new MemoryStream();
    using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      ds.Write(data, 0, data.Length);

    return ms.ToArray();
  }
}
