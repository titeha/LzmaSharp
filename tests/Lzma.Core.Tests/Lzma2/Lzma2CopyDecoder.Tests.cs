using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// <para>Тесты для самого простого декодера: LZMA2 Copy-чанки (несжатые) + End marker.</para>
/// <para>Эти тесты намеренно НЕ касаются LZMA-чанков — их поддержку добавим отдельным шагом.</para>
/// </summary>
public sealed class Lzma2CopyDecoderTests
{
  [Fact]
  public void Decode_CopyChunkAndEnd_FinishedAndOutputMatches()
  {
    // Данные, которые хотим «распаковать».
    byte[] payload = [1, 2, 3, 4, 5];

    // LZMA2-поток: copy(0x02) + payload + end(0x00)
    byte[] input = Concat([MakeCopyChunk(control: 0x02, payload), EndMarker()]);

    byte[] output = new byte[payload.Length];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length, consumed);
    Assert.Equal(payload.Length, written);
    Assert.Equal(payload, output);
  }

  [Fact]
  public void Decode_TwoCopyChunks_FinishedAndOutputIsConcatenation()
  {
    byte[] a = [0x10, 0x11];
    byte[] b = [0x20, 0x21, 0x22];

    // Обрати внимание: первый чанк без reset-dic (0x02), второй с reset-dic (0x01).
    // Для copy-чанков это сейчас не важно, но мы проверяем, что оба варианта control корректно читаются.
    byte[] input = Concat([MakeCopyChunk(control: 0x02, a), MakeCopyChunk(control: 0x01, b), EndMarker()]);

    byte[] expected = Concat([a, b]);
    byte[] output = new byte[expected.Length];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length, consumed);
    Assert.Equal(expected.Length, written);
    Assert.Equal(expected, output);
  }

  [Fact]
  public void Decode_TruncatedPayload_NeedMoreInputAndDoesNotConsume()
  {
    byte[] payload = [1, 2, 3, 4, 5, 6];
    byte[] fullStream = Concat([MakeCopyChunk(control: 0x02, payload), EndMarker()]);

    // Обрезаем поток так, чтобы заголовок чанка был целиком,
    // а payload был обрезан (не хватает данных для текущего чанка).
    //
    // Заголовок copy-чанка: 3 байта, поэтому 3 + 2 = 5 означает «есть только первые 2 байта payload».
    byte[] truncated = fullStream[..5];

    byte[] output = new byte[1024];

    var res = Lzma2CopyDecoder.Decode(
        truncated,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.NeedMoreInput, res);

    // Важно: на этом шаге декодер без внутреннего буфера.
    // Поэтому он НЕ «съедает» неполный чанк.
    Assert.Equal(0, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Decode_FirstChunkOk_SecondChunkTruncated_ConsumesFirstAndThenNeedsMoreInput()
  {
    byte[] a = [1, 2, 3];
    byte[] b = [9, 9, 9, 9];

    byte[] chunkA = MakeCopyChunk(control: 0x02, a);
    byte[] chunkB = MakeCopyChunk(control: 0x02, b);

    // Берём полностью первый чанк и только кусок второго чанка.
    // end-marker специально не добавляем.
    byte[] input = Concat([chunkA, chunkB[..4]]);

    byte[] output = new byte[1024];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.NeedMoreInput, res);

    // Первый чанк должен быть полностью «съеден» и записан в output.
    Assert.Equal(chunkA.Length, consumed);
    Assert.Equal(a.Length, written);

    // А в выходе первые байты должны совпасть.
    Assert.Equal(a, output.AsSpan(0, a.Length).ToArray());
  }

  [Fact]
  public void Decode_OutputTooSmall_NeedMoreOutputAndDoesNotConsume()
  {
    byte[] payload = [1, 2, 3, 4];
    byte[] input = Concat([MakeCopyChunk(control: 0x02, payload), EndMarker()]);

    // Меньше, чем payload.
    byte[] output = new byte[3];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.NeedMoreOutput, res);
    Assert.Equal(0, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Decode_FirstChunkOk_SecondChunkDoesNotFitInOutput_ConsumesFirstAndThenNeedsMoreOutput()
  {
    byte[] a = [1, 2, 3];
    byte[] b = [4, 5, 6, 7, 8];

    byte[] chunkA = MakeCopyChunk(control: 0x02, a);
    byte[] chunkB = MakeCopyChunk(control: 0x02, b);

    // end-marker добавляем, чтобы поток был формально завершён (но до него мы не дойдём).
    byte[] input = Concat([chunkA, chunkB, EndMarker()]);

    // Выхода хватает только на «a».
    byte[] output = new byte[a.Length];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.NeedMoreOutput, res);

    // Первый чанк обработан.
    Assert.Equal(chunkA.Length, consumed);
    Assert.Equal(a.Length, written);
    Assert.Equal(a, output);
  }

  [Fact]
  public void Decode_WhenNextChunkIsLzma_ReturnsNotSupportedAndDoesNotConsume()
  {
    // Минимальный LZMA-чанк: control=0x80,
    // затем unpackSizeMinus1 (2 bytes) и packSize_minus1 (2 bytes).
    // payload нам не нужен — декодер должен вернуть NotSupported сразу после заголовка.
    byte[] input = [0x80, 0x00, 0x00, 0x00, 0x00];

    byte[] output = new byte[16];

    var res = Lzma2CopyDecoder.Decode(
        input,
        output,
        out int consumed,
        out int written);

    Assert.Equal(Lzma2DecodeResult.NotSupported, res);
    Assert.Equal(0, consumed);
    Assert.Equal(0, written);
  }

  private static byte[] EndMarker() => [0x00];

  private static byte[] MakeCopyChunk(byte control, byte[] payload)
  {
    if (control is not (0x01 or 0x02))
      throw new ArgumentOutOfRangeException(nameof(control), "Для copy-чанка control должен быть 0x01 или 0x02.");

    if (payload.Length is < 1 or > 0x10000)
      throw new ArgumentOutOfRangeException(nameof(payload), "UnpackSize для copy-чанка должен быть в диапазоне 1..65536.");

    int unpackMinus1 = payload.Length - 1;

    // Формат copy-чанка:
    // [0] control
    // [1] unpackSizeMinus1 hi
    // [2] unpackSizeMinus1 lo
    // [3..] payload (raw bytes)
    byte[] chunk = new byte[3 + payload.Length];
    chunk[0] = control;
    chunk[1] = (byte)((unpackMinus1 >> 8) & 0xFF);
    chunk[2] = (byte)(unpackMinus1 & 0xFF);
    payload.AsSpan().CopyTo(chunk.AsSpan(3));
    return chunk;
  }

  private static byte[] Concat(params byte[][] arrays)
  {
    int total = 0;
    foreach (var a in arrays)
      total += a.Length;

    byte[] result = new byte[total];
    int pos = 0;

    foreach (var a in arrays)
    {
      a.CopyTo(result, pos);
      pos += a.Length;
    }

    return result;
  }
}
