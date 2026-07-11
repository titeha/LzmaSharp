using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// Тесты потокового декода packed-входа из Stream по ЦЕЛЫМ чанкам (Lzma2Decoder.DecodeStreamToStream):
/// вход читается кусками (в т.ч. по 1 байту), выход пишется в Stream — без удержания в памяти. Для
/// извлечения архивов больше 2 ГиБ. Проверяет обход блокера «нет возобновления частичного входа».
/// </summary>
public sealed class Lzma2DecoderStreamToStreamTests
{
  private static byte[] Encode(byte[] data, int dict)
  {
    Assert.True(LzmaProperties.TryCreate(3, 0, 2, out var props));
    return Lzma2LzmaEncoder.Encode(data, props, dict);
  }

  private static void RoundTrip(byte[] data, int dict, Stream packedSource)
  {
    Assert.True(Lzma2Properties.TryCreateFromDictionarySize((uint)dict, out var p2));
    using var outMs = new MemoryStream();

    Lzma2DecodeResult r = Lzma2Decoder.DecodeStreamToStream(
        packedSource, packedSource.Length, p2, outMs, out long written);

    Assert.Equal(Lzma2DecodeResult.Finished, r);
    Assert.Equal(data.LongLength, written);
    Assert.Equal(data, outMs.ToArray());
  }

  [Fact]
  public void Многочанковый_Текст_RoundTrip()
  {
    const int dict = 1 << 20;
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Поток декода 0123456789 ", 8000)));
    RoundTrip(data, dict, new MemoryStream(Encode(data, dict)));
  }

  [Fact]
  public void БольшойНесжимаемый_PackedБольшеБуфера_RoundTrip()
  {
    // Несжимаемое → packed большой (несколько чанков по 64 КБ) — раньше здесь и рвался блокер.
    const int dict = 1 << 20;
    var data = new byte[900_000];
    uint s = 0x999;
    for (int i = 0; i < data.Length; i++) { s = s * 1664525u + 1013904223u; data[i] = (byte)(s >> 24); }
    RoundTrip(data, dict, new MemoryStream(Encode(data, dict)));
  }

  [Fact]
  public void КапельныйПоток_ПоОдномуБайту_RoundTrip()
  {
    const int dict = 1 << 18;
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("капля 987 ", 3000)));
    RoundTrip(data, dict, new DripStream(Encode(data, dict)));
  }

  [Fact]
  public void КапельныйПоток_БольшойНесжимаемый_RoundTrip()
  {
    const int dict = 1 << 18;
    var data = new byte[200_000];
    uint s = 0xBEEF;
    for (int i = 0; i < data.Length; i++) { s = s * 1664525u + 1013904223u; data[i] = (byte)(s >> 24); }
    RoundTrip(data, dict, new DripStream(Encode(data, dict)));
  }

  [Fact]
  public void ОбрезанныйPacked_InvalidData_НеЗависает()
  {
    const int dict = 1 << 18;
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("обрезка ", 3000)));
    byte[] packed = Encode(data, dict);

    Assert.True(Lzma2Properties.TryCreateFromDictionarySize((uint)dict, out var p2));
    using var outMs = new MemoryStream();

    var shortSource = new MemoryStream(packed.Take(packed.Length / 2).ToArray());
    Lzma2DecodeResult r = Lzma2Decoder.DecodeStreamToStream(shortSource, packed.Length, p2, outMs, out _);
    Assert.Equal(Lzma2DecodeResult.InvalidData, r);
  }

  // Поток, отдающий по одному байту за Read — стресс кадрирования/дочитки.
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
      if (_pos >= data.Length || count == 0)
        return 0;
      buffer[offset] = data[_pos++];
      return 1;
    }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
