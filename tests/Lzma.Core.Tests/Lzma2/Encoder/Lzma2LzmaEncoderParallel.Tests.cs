using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// Тесты многопоточного блочного энкодера (EncodeParallelToStream): вход режется на независимые
/// блоки, сжимается параллельно и корректно распаковывается; CRC несжатого верен.
/// </summary>
public sealed class Lzma2LzmaEncoderParallelTests
{
  private static LzmaProperties Props()
  {
    Assert.True(LzmaProperties.TryCreate(3, 0, 2, out var p));
    return p;
  }

  private static void RoundTrip(byte[] data, int dictionarySize, int blockSize, int dop)
  {
    using var ms = new MemoryStream();
    long pack = Lzma2LzmaEncoder.EncodeParallelToStream(
        new MemoryStream(data), data.LongLength, Props(), dictionarySize, ms,
        out uint crc, blockSize: blockSize, maxDegreeOfParallelism: dop);

    Assert.Equal(ms.Length, pack);
    Assert.Equal(Lzma.Core.Checksums.Crc32.Compute(data), crc);

    Assert.True(Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out var p2));
    Lzma2DecodeResult r = Lzma2Decoder.DecodeToArray(ms.ToArray(), p2, out byte[] decoded, out _);
    Assert.Equal(Lzma2DecodeResult.Finished, r);
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void Пусто_RoundTrip()
  {
    RoundTrip([], 1 << 16, blockSize: 1 << 16, dop: 4);
  }

  [Fact]
  public void МеньшеОдногоБлока_RoundTrip()
  {
    byte[] data = Encoding.UTF8.GetBytes("привет мир, привет мир!");
    RoundTrip(data, 1 << 20, blockSize: 1 << 20, dop: 4);
  }

  [Fact]
  public void МногоБлоков_Текст_RoundTrip()
  {
    // ~500 КБ, блок 64 КБ → ~8 блоков через несколько потоков.
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Параллельно 0123456789 ", 21000)));
    RoundTrip(data, 1 << 18, blockSize: 64 * 1024, dop: 4);
  }

  [Fact]
  public void МногоБлоков_Псевдослучайные_RoundTrip()
  {
    var data = new byte[400_000];
    uint s = 0xCAFEBABE;
    for (int i = 0; i < data.Length; i++)
    {
      s = s * 1664525u + 1013904223u;
      data[i] = (byte)(s >> 24);
    }

    RoundTrip(data, 1 << 16, blockSize: 40_000, dop: 8);
  }

  [Fact]
  public void ГраницаБлока_ПоРазмеруСловаря_RoundTrip()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcABC012 ", 30000)));
    RoundTrip(data, 1 << 14, blockSize: 1 << 14, dop: 6); // block == dict
  }
}
