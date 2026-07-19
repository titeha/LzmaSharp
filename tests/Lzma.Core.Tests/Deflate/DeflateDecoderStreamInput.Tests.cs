using System.IO.Compression;
using System.Text;
using Lzma.Core.Deflate;
using Xunit;

namespace Lzma.Core.Tests.Deflate;

/// <summary>
/// Потоковый ВХОД инфлейтера: вход читается из Stream порциями (в т.ч. по 1 байту через границы чанков),
/// выход — потоком. Сверка с одноразовым декодером на разных данных, включая выход больше окна.
/// </summary>
public sealed class DeflateDecoderStreamInputTests
{
  // Поток, отдающий не более 1 байта за Read — стресс резюма бит-ридера через границы буфера.
  private sealed class DripStream(byte[] data) : Stream
  {
    private int _pos;
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => data.Length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count)
    {
      if (_pos >= data.Length || count == 0) return 0;
      buffer[offset] = data[_pos++];
      return 1;
    }
    public override void Flush() { }
    public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
    public override void SetLength(long v) => throw new NotSupportedException();
    public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
  }

  private static byte[] BclCompress(byte[] data)
  {
    using var ms = new MemoryStream();
    using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
      ds.Write(data, 0, data.Length);
    return ms.ToArray();
  }

  [Theory]
  [InlineData("A")]
  [InlineData("Hello streaming input")]
  [InlineData("ABCABCABCABCABCABCABCABC")]
  public void ПотоковыйВход_КороткийТекст(string text) => Assert(Encoding.UTF8.GetBytes(text));

  [Fact]
  public void ПотоковыйВход_БольшеОкна_Повтор()
      => Assert(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 20_000))));

  [Fact]
  public void ПотоковыйВход_БольшеОкна_Случайные()
  {
    var rnd = new Random(99);
    byte[] b = new byte[250_000];
    rnd.NextBytes(b);
    Assert(b);
  }

  [Fact]
  public void ПотоковыйВход_Нули_ОборотОкна() => Assert(new byte[300_000]);

  private static void Assert(byte[] original)
  {
    byte[] compressed = BclCompress(original);

    // Эталон — одноразовый декод.
    byte[] oneShot = new byte[original.Length];
    Xunit.Assert.Equal(DeflateDecodeResult.Ok, DeflateDecoder.Decode(compressed, oneShot, out _, out int w1));
    Xunit.Assert.Equal(original.Length, w1);

    // Потоковый вход через drip-поток (по 1 байту).
    using var input = new DripStream(compressed);
    using var output = new MemoryStream();
    Xunit.Assert.Equal(DeflateDecodeResult.Ok, DeflateDecoder.Decode(input, compressed.Length, output, deflate64: false, out long w2));
    Xunit.Assert.Equal(original.LongLength, w2);

    byte[] streamed = output.ToArray();
    Xunit.Assert.Equal(original, streamed);
    Xunit.Assert.Equal(oneShot, streamed); // байт-в-байт с одноразовым
  }
}
