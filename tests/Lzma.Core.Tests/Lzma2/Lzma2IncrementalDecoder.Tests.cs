using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderTests
{
  [Fact]
  public void Decode_CopyChunks_МожноКормитьПоОдномуБайту_ИПисатьВМаленькийБуфер()
  {
    // Два COPY-чанка подряд + END.
    byte[] payload1 = [1, 2, 3, 4, 5, 6];
    byte[] payload2 = [7, 8, 9, 10, 11];

    byte[] lzma2 = Concat([MakeCopyChunk(payload1, control: 0x01), MakeCopyChunk(payload2, control: 0x02), [0x00]]);

    byte[] expected = Concat(payload1, payload2);
    byte[] actual = new byte[expected.Length];

    var decoder = new Lzma2IncrementalDecoder();

    int inPos = 0;
    int outPos = 0;

    // Кормим вход по 1 байту, а выход даём по 2 байта.
    const int inChunkSize = 1;
    const int outChunkSize = 2;

    int safety = 0;
    while (true)
    {
      if (safety++ > 10_000)
        throw new Exception("Тест завис: слишком много итераций без завершения.");

      ReadOnlySpan<byte> inChunk = inPos < lzma2.Length
          ? lzma2.AsSpan(inPos, Math.Min(inChunkSize, lzma2.Length - inPos))
          : ReadOnlySpan<byte>.Empty;

      Span<byte> outChunk = outPos < actual.Length
          ? actual.AsSpan(outPos, Math.Min(outChunkSize, actual.Length - outPos))
          : Span<byte>.Empty;

      var res = decoder.Decode(inChunk, outChunk, out int consumed, out int written);

      Assert.InRange(consumed, 0, inChunk.Length);
      Assert.InRange(written, 0, outChunk.Length);

      inPos += consumed;
      outPos += written;

      if (res == Lzma2DecodeResult.Finished)
        break;

      Assert.NotEqual(Lzma2DecodeResult.InvalidData, res);
      Assert.NotEqual(Lzma2DecodeResult.NotSupported, res);

      // Защита от зависания: если декодер просит вход, а вход закончился — это ошибка теста/логики.
      if (res == Lzma2DecodeResult.NeedMoreInput && inPos >= lzma2.Length)
        throw new Exception("Декодер запросил ещё входных данных, но вход уже закончился.");

      // Защита: если декодер просит выход, а выход закончился — это тоже ошибка.
      if (res == Lzma2DecodeResult.NeedMoreOutput && outPos >= actual.Length)
        throw new Exception("Декодер запросил ещё места в выходе, но выходной буфер уже заполнен.");

      // Ещё один важный инвариант: если декодер просит больше входа/выхода,
      // он должен был хотя бы что-то сделать, если ему это позволяли переданные буферы.
      // В противном случае можно легко попасть в вечный цикл.
      if ((res == Lzma2DecodeResult.NeedMoreInput || res == Lzma2DecodeResult.NeedMoreOutput)
          && consumed == 0 && written == 0)
      {
        throw new Exception(
            "Декодер вернул NeedMore..., но не потребил ни одного байта и ничего не записал. " +
            "Это признак ошибки в автомате состояний.");
      }
    }

    Assert.Equal(lzma2.Length, inPos);
    Assert.Equal(expected.Length, outPos);
    Assert.Equal(expected, actual);

    Assert.Equal(lzma2.Length, decoder.TotalBytesRead);
    Assert.Equal(expected.Length, decoder.TotalBytesWritten);
  }

  [Fact]
  public void Progress_Report_ВКонцеРавенИтоговымСчётчикам()
  {
    byte[] payload = [10, 20, 30, 40, 50];
    byte[] lzma2 = Concat([MakeCopyChunk(payload, control: 0x01), [0x00]]);

    var sink = new ListProgress();
    var decoder = new Lzma2IncrementalDecoder(sink);

    byte[] actual = new byte[payload.Length];

    // Специально режем вход так, чтобы заголовок COPY-чанка разделился по разным вызовам.
    // Заголовок COPY = 3 байта, поэтому 2 + 1 + ...
    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      int take = inPos switch
      {
        0 => 2,
        2 => 1,
        _ => 64
      };

      ReadOnlySpan<byte> inChunk = inPos < lzma2.Length
          ? lzma2.AsSpan(inPos, Math.Min(take, lzma2.Length - inPos))
          : ReadOnlySpan<byte>.Empty;

      Span<byte> outChunk = outPos < actual.Length
          ? actual.AsSpan(outPos)
          : [];

      var res = decoder.Decode(inChunk, outChunk, out int consumed, out int written);

      inPos += consumed;
      outPos += written;

      if (res == Lzma2DecodeResult.Finished)
        break;

      Assert.NotEqual(Lzma2DecodeResult.InvalidData, res);
      Assert.NotEqual(Lzma2DecodeResult.NotSupported, res);

      if (res == Lzma2DecodeResult.NeedMoreInput && inPos >= lzma2.Length)
        throw new Exception("Декодер запросил ещё вход, но вход уже закончился.");
    }

    Assert.Equal(payload, actual);
    Assert.NotEmpty(sink.Items);

    // Последний отчёт должен совпадать с финальными счётчиками.
    LzmaProgress last = sink.Items[^1];
    Assert.Equal(decoder.TotalBytesRead, last.BytesRead);
    Assert.Equal(decoder.TotalBytesWritten, last.BytesWritten);

    // И сами счётчики должны быть ожидаемыми.
    Assert.Equal(lzma2.Length, decoder.TotalBytesRead);
    Assert.Equal(payload.Length, decoder.TotalBytesWritten);
  }

  [Fact]
  public void Decode_LzmaChunk_ПокаНеПоддерживается()
  {
    // Минимальный валидный заголовок LZMA-чанка (без props): 5 байт.
    // control=0x80 => LZMA chunk, без props (т.к. < 0xE0).
    // unpackSize = 1, packSize = 1.
    byte[] lzmaHeaderOnly = [0x80, 0x00, 0x00, 0x00, 0x00];

    var decoder = new Lzma2IncrementalDecoder();
    var res = decoder.Decode(lzmaHeaderOnly, Span<byte>.Empty, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.NotSupported, res);
    Assert.Equal(lzmaHeaderOnly.Length, consumed);
    Assert.Equal(0, written);
  }

  // ----------------------
  // Вспомогательные штуки
  // ----------------------

  private static byte[] MakeCopyChunk(byte[] payload, byte control)
  {
    // COPY-чанк: [control][unpackSizeHi][unpackSizeLo][payload...]
    // unpackSize хранится как (size-1) в 16 битах.
    if (payload.Length <= 0)
      throw new ArgumentOutOfRangeException(nameof(payload));
    if (payload.Length > 0x10000)
      throw new ArgumentOutOfRangeException(nameof(payload), "COPY-чанк поддерживает unpackSize максимум 64К.");

    int u = payload.Length - 1;
    byte hi = (byte)((u >> 8) & 0xFF);
    byte lo = (byte)(u & 0xFF);

    byte[] result = new byte[3 + payload.Length];
    result[0] = control;
    result[1] = hi;
    result[2] = lo;
    Buffer.BlockCopy(payload, 0, result, 3, payload.Length);
    return result;
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
      Buffer.BlockCopy(a, 0, result, pos, a.Length);
      pos += a.Length;
    }
    return result;
  }

  private sealed class ListProgress : IProgress<LzmaProgress>
  {
    public List<LzmaProgress> Items { get; } = new();

    public void Report(LzmaProgress value) => Items.Add(value);
  }
}
