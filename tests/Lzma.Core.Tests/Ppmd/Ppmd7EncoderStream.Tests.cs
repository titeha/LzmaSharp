using System.Text;
using Lzma.Core.Ppmd;
using Xunit;

namespace Lzma.Core.Tests.Ppmd;

/// <summary>
/// Потоковое кодирование PPMd (вход/выход потоком): байт-в-байт совпадает с одноразовым энкодером на
/// тех же данных (та же модель, тот же range-кодер, отличается только приёмник) + round-trip декодером.
/// </summary>
public sealed class Ppmd7EncoderStreamTests
{
  private const int Order = 6;
  private const uint MemSize = 16u << 20;

  private static byte[] EncodeOneShot(byte[] data)
  {
    Assert.Equal(Ppmd7EncodeResult.Ok, Ppmd7Encoder.Encode(data, Order, MemSize, out byte[] output));
    return output;
  }

  private static byte[] EncodeStreaming(byte[] data, out long written)
  {
    using var input = new MemoryStream(data);
    using var output = new MemoryStream();
    Assert.Equal(Ppmd7EncodeResult.Ok, Ppmd7Encoder.Encode(input, data.Length, Order, MemSize, output, out written));
    return output.ToArray();
  }

  private static void RoundTrip(byte[] data)
  {
    byte[] oneShot = EncodeOneShot(data);
    byte[] streamed = EncodeStreaming(data, out long written);

    Assert.Equal(oneShot, streamed);            // байт-в-байт с одноразовым
    Assert.Equal(streamed.LongLength, written); // счётчик = размер выхода

    byte[] decoded = new byte[data.Length];
    Assert.Equal(Ppmd7DecodeResult.Ok, Ppmd7Decoder.Decode(streamed, Order, MemSize, decoded));
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void Пусто() => RoundTrip([]);

  [Theory]
  [InlineData(1)]
  [InlineData(1000)]
  [InlineData(70000)] // > буфера чтения 64 КБ
  public void Текст(int n)
      => RoundTrip(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("The quick brown fox jumps. ", n / 27 + 1)).Substring(0, n)));

  [Fact]
  public void Случайные_БольшеБуфера()
  {
    var rnd = new Random(17);
    byte[] data = new byte[200_000];
    rnd.NextBytes(data);
    RoundTrip(data);
  }

  [Fact]
  public void Нули()
  {
    RoundTrip(new byte[150_000]);
  }

  [Fact]
  public void ВходКороче_Бросает()
  {
    using var input = new MemoryStream(new byte[10]);
    using var output = new MemoryStream();
    Assert.Throws<EndOfStreamException>(() => Ppmd7Encoder.Encode(input, 20, Order, MemSize, output, out _));
  }
}
