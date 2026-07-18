using System.IO.Compression;
using System.Text;

using Lzma.Core.Deflate;

namespace Lzma.Core.Tests.Deflate;

/// <summary>
/// Потоковый (в <see cref="Stream"/>) декод DEFLATE через кольцевое окно истории. Доказывает
/// БАЙТ-В-БАЙТ совпадение с одноразовым <see cref="DeflateDecoder"/> на входах, чей распакованный
/// размер превышает окно (оборот кольца и back-reference через границу).
/// </summary>
public sealed class DeflateDecoderStreamingTests
{
  [Theory]
  [InlineData("A")]
  [InlineData("Hello, streaming DEFLATE world!")]
  [InlineData("ABCABCABCABCABCABCABCABCABCABCABC")]
  public void Поток_СовпадаетСОдноразовым_КороткийТекст(string text)
      => AssertStreamingMatches(Encoding.UTF8.GetBytes(text));

  [Fact]
  public void Поток_Нули_ОборотОкнаИПерекрытие()
      => AssertStreamingMatches(new byte[300_000]); // RLE distance=1, выход > окна 128 КБ

  [Fact]
  public void Поток_ПовторяющийсяТекст_БольшеОкна()
  {
    byte[] original = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet, consectetur. ", 10_000)));
    Assert.True(original.Length > 300_000);
    AssertStreamingMatches(original);
  }

  [Fact]
  public void Поток_СлучайныеДанные_БольшеОкна()
  {
    var random = new Random(20260718);
    byte[] original = new byte[250_000]; // почти несжимаемо → много литералов/stored, выход > окна
    random.NextBytes(original);
    AssertStreamingMatches(original);
  }

  [Fact]
  public void Поток_СмесьТекстаИСлучайных_БольшеОкна()
  {
    var random = new Random(4242);
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("structured text block ", 8_000)));
    byte[] noise = new byte[80_000];
    random.NextBytes(noise);
    byte[] original = [.. text, .. noise, .. text];
    AssertStreamingMatches(original);
  }

  [Fact]
  public void Поток_КороткийПериод_ПерекрывающиесяСовпадения()
  {
    // Короткий период → distance < length (перекрывающееся копирование из окна).
    byte[] pattern = Encoding.UTF8.GetBytes("xyzw");
    byte[] original = new byte[200_000];
    for (int i = 0; i < original.Length; i++)
      original[i] = pattern[i % pattern.Length];
    AssertStreamingMatches(original);
  }

  [Fact]
  public void Поток_ПовреждённыйВход_НеПадает()
  {
    byte[] compressed = BclCompress(Encoding.UTF8.GetBytes("some data to compress and then corrupt streaming"));
    compressed[compressed.Length / 2] ^= 0xFF;

    using var ms = new MemoryStream();
    DeflateDecodeResult result = DeflateDecoder.Decode(compressed, ms, deflate64: false, out _);

    // Либо явная ошибка, либо успех, но не необработанное исключение.
    Assert.True(result is DeflateDecodeResult.Ok or DeflateDecodeResult.InvalidData);
  }

  // Декодирует вход и одноразовым, и потоковым путём; требует совпадения обоих с оригиналом.
  private static void AssertStreamingMatches(byte[] original)
  {
    byte[] compressed = BclCompress(original);

    byte[] oneShot = new byte[original.Length];
    DeflateDecodeResult r1 = DeflateDecoder.Decode(compressed, oneShot, out _, out int written1);
    Assert.Equal(DeflateDecodeResult.Ok, r1);
    Assert.Equal(original.Length, written1);

    using var ms = new MemoryStream();
    DeflateDecodeResult r2 = DeflateDecoder.Decode(compressed, ms, deflate64: false, out long written2);
    Assert.Equal(DeflateDecodeResult.Ok, r2);
    Assert.Equal(original.LongLength, written2);

    byte[] streamed = ms.ToArray();
    Assert.Equal(original, streamed);
    Assert.Equal(oneShot, streamed); // байт-в-байт с одноразовым декодером
  }

  private static byte[] BclCompress(byte[] data)
  {
    using var ms = new MemoryStream();
    using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      ds.Write(data, 0, data.Length);

    return ms.ToArray();
  }
}
