using System.Text;

using ICSharpCode.SharpZipLib.BZip2;

using Lzma.Core.BZip2;

namespace Lzma.Core.Tests.BZip2;

public sealed class BZip2DecoderTests
{
  [Theory]
  [InlineData("A")]
  [InlineData("Hello, BZip2 world!")]
  [InlineData("ABCABCABCABCABCABCABCABCABCABCABC")]
  [InlineData("the quick brown fox jumps over the lazy dog the quick brown fox")]
  public void Decode_СовпадаетСSharpZipLibДляТекста(string text)
  {
    AssertRoundTrip(Encoding.UTF8.GetBytes(text));
  }

  [Fact]
  public void Decode_Пусто()
  {
    AssertRoundTrip([]);
  }

  [Fact]
  public void Decode_Нули()
  {
    AssertRoundTrip(new byte[50_000]);
  }

  [Fact]
  public void Decode_ПовторяющийсяПаттерн()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 2000)));
    AssertRoundTrip(data);
  }

  [Fact]
  public void Decode_СлучайныеДанные()
  {
    var random = new Random(20260618);
    byte[] data = new byte[60_000];
    random.NextBytes(data);
    AssertRoundTrip(data);
  }

  [Fact]
  public void Decode_НесколькоБлоков_БлокПо100к()
  {
    // > 100 КБ при размере блока 1 (100к) => несколько блоков + end-of-stream.
    var random = new Random(42);
    byte[] textPart = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("structured ", 30_000)));
    byte[] randomPart = new byte[150_000];
    random.NextBytes(randomPart);
    byte[] data = [.. textPart, .. randomPart];

    AssertRoundTrip(data, blockSize100k: 1);
  }

  [Fact]
  public void Decode_RLE1_ДлинныеПовторыОдногоБайта()
  {
    // Проверяем обратный RLE1 первой стадии: длинные прогоны одинаковых байт.
    byte[] data = new byte[5000];
    Array.Fill(data, (byte)'Z');
    AssertRoundTrip(data);
  }

  [Fact]
  public void Decode_ПовреждённыйПоток_НеПадаетНеобработанно()
  {
    byte[] compressed = SharpZipLibCompress(Encoding.UTF8.GetBytes("some data to compress and corrupt"), 9);
    compressed[compressed.Length / 2] ^= 0xFF;

    BZip2DecodeResult result = BZip2Decoder.Decode(compressed, out _);

    Assert.True(result is BZip2DecodeResult.Ok or BZip2DecodeResult.InvalidData or BZip2DecodeResult.NotSupported);
  }

  private static void AssertRoundTrip(byte[] original, int blockSize100k = 9)
  {
    byte[] compressed = SharpZipLibCompress(original, blockSize100k);

    BZip2DecodeResult result = BZip2Decoder.Decode(compressed, out byte[] output);

    Assert.Equal(BZip2DecodeResult.Ok, result);
    Assert.Equal(original, output);
  }

  private static byte[] SharpZipLibCompress(byte[] data, int blockSize100k)
  {
    using var ms = new MemoryStream();
    using (var bs = new BZip2OutputStream(ms, blockSize100k) { IsStreamOwner = false })
      bs.Write(data, 0, data.Length);

    return ms.ToArray();
  }
}
