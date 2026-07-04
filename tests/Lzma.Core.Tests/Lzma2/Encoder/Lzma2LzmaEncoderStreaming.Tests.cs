using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// СПАЙК потокового энкодера: доказывает, что EncodeStreaming (вход блоками через кольцевой буфер,
/// без всего файла в памяти) даёт БАЙТ-В-БАЙТ тот же LZMA2-поток, что Encode(ReadOnlySpan) — в т.ч.
/// на входах БОЛЬШЕ словаря (кольцо вытесняет историю). И что поток корректно распаковывается.
/// </summary>
public sealed class Lzma2LzmaEncoderStreamingTests
{
  private static LzmaProperties Props()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var p));
    return p;
  }

  private static void AssertIdentical(byte[] data, int dictionarySize)
  {
    LzmaProperties props = Props();

    byte[] reference = Lzma2LzmaEncoder.Encode(data, props, dictionarySize);
    byte[] streamed = Lzma2LzmaEncoder.EncodeStreaming(new MemoryStream(data), data.LongLength, props, dictionarySize);

    Assert.Equal(reference, streamed); // байт-в-байт

    // И для надёжности — распаковывается обратно в исходные данные.
    Assert.True(Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out var lzma2Props));
    Lzma2DecodeResult r = Lzma2Decoder.DecodeToArray(streamed, lzma2Props, out byte[] decoded, out _);
    Assert.Equal(Lzma2DecodeResult.Finished, r);
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void Пусто_Идентично()
  {
    AssertIdentical([], 1 << 16);
  }

  [Fact]
  public void Маленький_ЦеликомВСловаре_Идентично()
  {
    byte[] data = Encoding.UTF8.GetBytes("привет мир, привет мир, привет мир!");
    AssertIdentical(data, 1 << 20);
  }

  [Fact]
  public void Текст_БольшеСловаря_КольцоВытесняет_Идентично()
  {
    // Данные (~200 КБ) заведомо больше словаря (4 КБ) → кольцо вытесняет историю: настоящий стрим.
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Поток LZMA2 0123456789 ", 9000)));
    AssertIdentical(data, 4096);
  }

  [Fact]
  public void Периодика_БольшеСловаря_Идентично()
  {
    var data = new byte[150_000];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i % 61);

    AssertIdentical(data, 1 << 12);
  }

  [Fact]
  public void ПсевдослучайныеДанные_БольшеСловаря_Идентично()
  {
    // Детерминированный LCG (без Random) — плохо сжимается, много литералов и коротких матчей.
    var data = new byte[120_000];
    uint state = 0x12345678;
    for (int i = 0; i < data.Length; i++)
    {
      state = state * 1664525u + 1013904223u;
      data[i] = (byte)(state >> 24);
    }

    AssertIdentical(data, 1 << 13);
  }

  [Fact]
  public void РазныеРазмерыСловаря_Идентично()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("abcabcXYZ 987 ", 5000)));

    // Словари >= 4 КБ (канонический минимум LZMA2 для round-trip через декодер).
    foreach (int dict in new[] { 1 << 12, 1 << 14, 1 << 16, 1 << 20 })
      AssertIdentical(data, dict);
  }
}
