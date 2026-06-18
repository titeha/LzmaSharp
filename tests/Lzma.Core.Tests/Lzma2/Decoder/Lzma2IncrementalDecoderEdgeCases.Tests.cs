using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderEdgeCasesTests
{
  [Fact]
  public void InvalidControl_ВозвращаетInvalidData_Потребляет1Байт_ИФиксируетСостояниеОшибки()
  {
    var dec = new Lzma2IncrementalDecoder();

    // 0x03..0x7F — зарезервировано/недопустимо в LZMA2.
    ReadOnlySpan<byte> input = [0x03];
    Span<byte> output = stackalloc byte[16];

    var res = dec.Decode(input, output, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.InvalidData, res);
    Assert.Equal(1, consumed);
    Assert.Equal(0, written);

    // Повторный вызов после ошибки: должны оставаться в ошибке и ничего не потреблять/писать.
    res = dec.Decode([0x00], output, out consumed, out written);

    Assert.Equal(Lzma2DecodeResult.InvalidData, res);
    Assert.Equal(0, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Reset_ПослеОшибкиПозволяетДекодироватьЗаново()
  {
    var dec = new Lzma2IncrementalDecoder();

    // Загоняем декодер в ошибку.
    _ = dec.Decode([0x03], [], out _, out _);

    dec.Reset();

    byte[] payload = [0xAA];
    byte[] lzma2 = MakeCopyChunk(payload, endMarker: true);
    byte[] dst = new byte[payload.Length];

    var res = dec.Decode(lzma2, dst, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(lzma2.Length, consumed);
    Assert.Equal(payload.Length, written);
    Assert.Equal(payload, dst);
  }

  [Fact]
  public void EndMarker_ЗавершаетДекодер_ИНеПотребляетЛишниеБайты()
  {
    var dec = new Lzma2IncrementalDecoder();

    // END + произвольные байты дальше.
    byte[] input = [0x00, 0x01, 0x00, 0x00, 0xFF];

    var res = dec.Decode(input, [], out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(1, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void CopyChunk_MinSize_1Байт_ДекодируетсяЗаОдинВызов()
  {
    var dec = new Lzma2IncrementalDecoder();

    byte[] payload = "B"u8.ToArray();
    byte[] input = MakeCopyChunk(payload, endMarker: true);
    byte[] output = new byte[payload.Length];

    var res = dec.Decode(input, output, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length, consumed);
    Assert.Equal(payload.Length, written);
    Assert.Equal(payload, output);
  }

  [Fact]
  public void CopyChunk_MaxSize_65536Байт_ДекодируетсяЗаОдинВызов()
  {
    var dec = new Lzma2IncrementalDecoder();

    // Максимальный размер COPY-чанка, который кодируется 16-битным полем (size-1).
    byte[] payload = new byte[65_536];
    for (int i = 0; i < payload.Length; i++)
      payload[i] = (byte)i;

    byte[] input = MakeCopyChunk(payload, endMarker: true);
    byte[] output = new byte[payload.Length];

    var res = dec.Decode(input, output, out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length, consumed);
    Assert.Equal(payload.Length, written);
    Assert.Equal(payload, output);
  }

  [Fact]
  public void CopyChunk_МаленькийВыходнойБуфер_ДолженВозвращатьNeedMoreOutput_ИНеТерятьДанные()
  {
    var dec = new Lzma2IncrementalDecoder();

    byte[] payload = new byte[32];
    for (int i = 0; i < payload.Length; i++)
      payload[i] = (byte)(0xA0 + i);

    byte[] input = MakeCopyChunk(payload, endMarker: true);
    byte[] output = new byte[payload.Length];

    int inPos = 0;
    int outPos = 0;
    int safety = 0;
    bool sawNeedMoreOutput = false;

    while (true)
    {
      if (safety++ > 10_000)
        throw new Exception("Декодер зациклился.");

      // Даём весь оставшийся ввод (тут нам важнее ограничивать output).
      ReadOnlySpan<byte> inChunk = input.AsSpan(inPos);

      // Жёстко ограничиваем output.
      Span<byte> outChunk = outPos < output.Length
          ? output.AsSpan(outPos, Math.Min(4, output.Length - outPos))
          : [];

      var res = dec.Decode(inChunk, outChunk, out int consumed, out int written);

      if (consumed == 0 && written == 0 && res != Lzma2DecodeResult.Finished)
        throw new Exception("Нет прогресса: декодер не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == Lzma2DecodeResult.NeedMoreOutput)
        sawNeedMoreOutput = true;

      if (res == Lzma2DecodeResult.Finished)
        break;

      Assert.True(
          res == Lzma2DecodeResult.NeedMoreOutput || res == Lzma2DecodeResult.NeedMoreInput,
          $"Неожиданный результат: {res}");
    }

    Assert.True(sawNeedMoreOutput);
    Assert.Equal(input.Length, inPos);
    Assert.Equal(payload.Length, outPos);
    Assert.Equal(payload, output);
  }

  [Fact]
  public void LzmaChunk_СProps_ЗаголовокПотребляется_ИВозвращаетсяNeedMoreOutput()
  {
    var dec = new Lzma2IncrementalDecoder();

    // LZMA-чанк с флагами reset+new props (0xE0), unpack=1, pack=1, props=0x5D.
    // payload мы не добавляем: декодер должен остановиться на NeedMoreOutput сразу после заголовка.
    byte[] header = [0xE0, 0x00, 0x00, 0x00, 0x00, 0x5D];

    var res = dec.Decode(header, [], out int consumed, out int written);

    Assert.Equal(Lzma2DecodeResult.NeedMoreOutput, res);
    Assert.Equal(6, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void CopyChunk_ПустойOutput_МожетСъестьЗаголовокНоНеДолженСъестьPayload()
  {
    var dec = new Lzma2IncrementalDecoder();

    byte[] payload = [1, 2, 3, 4];
    byte[] input = MakeCopyChunk(payload, endMarker: true);

    // 1) Подаём весь ввод, но output = пустой.
    var res = dec.Decode(input, [], out int consumed, out int written);

    // Декодер имеет право прочитать и сохранить у себя заголовок (3 байта),
    // но payload он не должен потреблять, потому что некуда писать.
    Assert.Equal(Lzma2DecodeResult.NeedMoreOutput, res);
    Assert.Equal(3, consumed);
    Assert.Equal(0, written);

    // 2) Продолжаем с оставшегося ввода.
    byte[] output = new byte[payload.Length];
    res = dec.Decode(input.AsSpan(consumed), output, out int consumed2, out int written2);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length - consumed, consumed2);
    Assert.Equal(payload.Length, written2);
    Assert.Equal(payload, output);
  }

  private static byte[] MakeCopyChunk(ReadOnlySpan<byte> payload, bool endMarker)
  {
    if (payload.Length is < 1 or > 65_536)
      throw new ArgumentOutOfRangeException(nameof(payload));

    int sizeMinus1 = payload.Length - 1;
    int total = 3 + payload.Length + (endMarker ? 1 : 0);
    var buf = new byte[total];

    // COPY + reset dic.
    buf[0] = 0x01;

    // В заголовке sizes — big-endian, хранят size-1.
    buf[1] = (byte)((sizeMinus1 >> 8) & 0xFF);
    buf[2] = (byte)(sizeMinus1 & 0xFF);

    payload.CopyTo(buf.AsSpan(3));

    if (endMarker)
      buf[^1] = 0x00;

    return buf;
  }
}
