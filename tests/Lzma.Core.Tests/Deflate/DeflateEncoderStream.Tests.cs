using System.IO.Compression;
using System.Text;
using Lzma.Core.Deflate;
using Xunit;

namespace Lzma.Core.Tests.Deflate;

/// <summary>
/// Потоковое кодирование DEFLATE (вход/выход потоком, блоки по 1 МиБ): выход валиден и для нашего
/// декодера, и для BCL, на разных размерах, включая пересечение границ блоков и несжимаемые данные.
/// </summary>
public sealed class DeflateEncoderStreamTests
{
  private static byte[] EncodeStream(byte[] data)
  {
    using var input = new MemoryStream(data);
    using var output = new MemoryStream();
    DeflateEncoder.Encode(input, data.Length, output);
    return output.ToArray();
  }

  private static byte[] BclInflate(byte[] deflate, int expected)
  {
    using var input = new MemoryStream(deflate);
    using var ds = new DeflateStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();
    ds.CopyTo(output);
    byte[] r = output.ToArray();
    Assert.Equal(expected, r.Length);
    return r;
  }

  private static byte[] OurInflate(byte[] deflate, int expected)
  {
    byte[] outBuf = new byte[expected];
    Assert.Equal(DeflateDecodeResult.Ok, DeflateDecoder.Decode(deflate, outBuf, out _, out int w));
    Assert.Equal(expected, w);
    return outBuf;
  }

  private static void RoundTrip(byte[] data)
  {
    byte[] deflate = EncodeStream(data);
    Assert.Equal(data, OurInflate(deflate, data.Length)); // наш декодер
    Assert.Equal(data, BclInflate(deflate, data.Length)); // независимый: BCL
  }

  [Fact]
  public void Пусто() => RoundTrip([]);

  [Theory]
  [InlineData(1)]
  [InlineData(100)]
  [InlineData(65535)]
  [InlineData(65536)]
  public void МелкиеРазмеры_Сжимаемые(int n)
      => RoundTrip(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abc", n / 3 + 1)).Substring(0, n)));

  [Fact]
  public void ЧерезГраницуБлока_Сжимаемый()
  {
    // > 1 МиБ (StreamBlockSize) → несколько блоков.
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("The quick brown fox. ", 200_000)));
    Assert.True(data.Length > 3 * (1 << 20));
    RoundTrip(data);
  }

  [Fact]
  public void ЧерезГраницуБлока_Несжимаемый_Stored()
  {
    var rnd = new Random(7);
    byte[] data = new byte[3 * (1 << 20) + 12345]; // >3 блоков случайных → stored
    rnd.NextBytes(data);
    RoundTrip(data);
  }

  [Fact]
  public void РовноГраницаБлока()
  {
    var rnd = new Random(11);
    byte[] data = new byte[2 * (1 << 20)]; // ровно 2 блока
    for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 31 + (rnd.Next() & 3));
    RoundTrip(data);
  }

  [Fact]
  public void Нули()
  {
    RoundTrip(new byte[(1 << 20) + 500]); // >1 блока нулей
  }

  [Fact]
  public void ВходКороче_Бросает()
  {
    using var input = new MemoryStream(new byte[10]);
    using var output = new MemoryStream();
    Assert.Throws<EndOfStreamException>(() => DeflateEncoder.Encode(input, 20, output));
  }
}
