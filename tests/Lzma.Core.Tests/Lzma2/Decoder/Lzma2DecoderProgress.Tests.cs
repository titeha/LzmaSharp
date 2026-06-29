using System;
using System.Collections.Generic;

using Lzma.Core;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// Шаг A within-folder гранулярности: <see cref="Lzma2Decoder.DecodeToArray"/> пробрасывает
/// <see cref="IProgress{T}"/> в инкрементальный декодер, который репортит по ходу (≈ на каждый
/// выходной чанк), а не один раз в конце.
/// </summary>
public class Lzma2DecoderProgressTests
{
  // Записывающий приёмник отчётов.
  private sealed class Recorder : IProgress<LzmaProgress>
  {
    public List<LzmaProgress> Reports { get; } = [];
    public void Report(LzmaProgress value) => Reports.Add(value);
  }

  [Fact]
  public void DecodeToArray_БольшойПоток_РепортитПрогрессПоХоду()
  {
    // Поток крупнее одного выходного чанка (64 КБ) → несколько промежуточных отчётов.
    byte[] data = new byte[256 * 1024];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 31 + 7);

    const int dictionarySize = 1 << 20;
    byte[] encoded = Lzma2CopyEncoder.Encode(data, dictionarySize, out byte dictProp);

    var recorder = new Recorder();

    Lzma2DecodeResult result = Lzma2Decoder.DecodeToArray(
        encoded,
        dictProp,
        out byte[] decoded,
        out _,
        recorder);

    Assert.Equal(Lzma2DecodeResult.Finished, result);
    Assert.Equal(data, decoded);

    // Главное: отчётов БОЛЬШЕ одного (прогресс по ходу, а не только финал).
    Assert.True(recorder.Reports.Count > 1, $"ожидали >1 отчёта, получили {recorder.Reports.Count}");

    // Монотонность по записанным байтам.
    for (int i = 1; i < recorder.Reports.Count; i++)
      Assert.True(recorder.Reports[i].BytesWritten >= recorder.Reports[i - 1].BytesWritten);

    // Финальный отчёт = полный размер распаковки.
    Assert.Equal(data.Length, recorder.Reports[^1].BytesWritten);
  }

  [Fact]
  public void DecodeToArray_БезПриёмника_РаботаетКакПрежде()
  {
    byte[] data = [1, 2, 3, 4, 5, 0, 255, 9, 9, 9];
    const int dictionarySize = 1 << 20;
    byte[] encoded = Lzma2CopyEncoder.Encode(data, dictionarySize, out byte dictProp);

    Lzma2DecodeResult result = Lzma2Decoder.DecodeToArray(encoded, dictProp, out byte[] decoded, out _);

    Assert.Equal(Lzma2DecodeResult.Finished, result);
    Assert.Equal(data, decoded);
  }
}
