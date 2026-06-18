using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneIncrementalDecoderTests
{
  [Fact]
  public void Decode_ТребуетЕщеВвода_КогдаЗаголовокНеПолный()
  {
    // Заголовок LZMA-Alone = 13 байт.
    Span<byte> header = stackalloc byte[13];

    // Заполним так, чтобы был валидный properties byte, но заголовок будет неполный.
    header[0] = 93; // lc=3, lp=0, pb=2

    var dec = new LzmaAloneIncrementalDecoder();

    // Дадим только первые 3 байта заголовка.
    var res = dec.Decode(header[..3], [], out int consumed, out int written);

    Assert.Equal(LzmaAloneDecodeResult.NeedMoreInput, res);
    Assert.Equal(3, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Decode_НеверныеДанные_ЕслиPropertiesByteНекорректен()
  {
    // properties byte должен быть < 9*5*5 = 225
    Span<byte> header = stackalloc byte[13];
    header[0] = 255;

    var dec = new LzmaAloneIncrementalDecoder();

    var res = dec.Decode(header, [], out int consumed, out int written);

    Assert.Equal(LzmaAloneDecodeResult.InvalidData, res);
    Assert.Equal(13, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Decode_НеПоддерживается_ЕслиUncompressedSizeНеИзвестен()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var props));

    var header = new LzmaAloneHeader(
      properties: props,
      dictionarySize: 1 << 20,
      uncompressedSize: ulong.MaxValue);

    Span<byte> headerBytes = stackalloc byte[LzmaAloneHeader.HeaderSize];
    Assert.True(header.TryWrite(headerBytes, out _));

    var dec = new LzmaAloneIncrementalDecoder();

    var res = dec.Decode(headerBytes, Span<byte>.Empty, out int consumed, out int written);

    Assert.Equal(LzmaAloneDecodeResult.NotSupported, res);
    Assert.Equal(LzmaAloneHeader.HeaderSize, consumed);
    Assert.Equal(0, written);
  }

  [Fact]
  public void Decode_RoundTrip_OneShot_ДаетТочныйРезультат()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var props));

    byte[] plain = [65, 66, 67, 0, 255];
    const int dictionarySize = 1 << 20;

    // Собираем .lzma: header + payload(LZMA stream)
    byte[] encoded = BuildLzmaAloneLiteralOnly(props, dictionarySize, plain);

    var dec = new LzmaAloneIncrementalDecoder();

    byte[] dst = new byte[plain.Length];

    var res = dec.Decode(encoded, dst, out int consumed, out int written);

    Assert.Equal(LzmaAloneDecodeResult.Finished, res);
    Assert.Equal(plain.Length, written);
    Assert.Equal(plain, dst);

    // Важно: мы НЕ обязаны "съедать" весь вход (range encoder мог дописать хвост).
    Assert.InRange(consumed, 13, encoded.Length);
  }

  [Fact]
  public void Decode_РаботаетПриПотоковойПодаче_КрошечнымиКусками()
  {
    Assert.True(LzmaProperties.TryCreate(lc: 3, lp: 0, pb: 2, out var props));

    byte[] plain = [65, 66, 67, 0, 255, 10, 11, 12, 13, 14, 15, 16];
    const int dictionarySize = 1 << 20;

    byte[] encoded = BuildLzmaAloneLiteralOnly(props, dictionarySize, plain);

    var dec = new LzmaAloneIncrementalDecoder();

    byte[] dst = DecodeAllStreamed(dec, encoded, expectedOutputSize: plain.Length, maxInChunk: 2, maxOutChunk: 1);

    Assert.Equal(plain, dst);
  }

  private static byte[] BuildLzmaAloneLiteralOnly(Lzma.Core.Lzma1.LzmaProperties props, int dictionarySize, byte[] plain)
  {
    var header = new LzmaAloneHeader(
      properties: props,
      dictionarySize: dictionarySize,
      uncompressedSize: (ulong)plain.Length);

    Span<byte> headerBytes = stackalloc byte[LzmaAloneHeader.HeaderSize];
    Assert.True(header.TryWrite(headerBytes, out _));

    var enc = new LzmaEncoder(props, dictionarySize);
    byte[] payload = enc.EncodeLiteralOnly(plain);

    var result = new byte[headerBytes.Length + payload.Length];
    headerBytes.CopyTo(result);
    payload.CopyTo(result, headerBytes.Length);
    return result;
  }

  private static byte[] DecodeAllStreamed(
    LzmaAloneIncrementalDecoder decoder,
    byte[] encoded,
    int expectedOutputSize,
    int maxInChunk,
    int maxOutChunk)
  {
    byte[] output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      ReadOnlySpan<byte> inChunk = encoded.AsSpan(inPos, Math.Min(maxInChunk, encoded.Length - inPos));
      Span<byte> outChunk = output.AsSpan(outPos, Math.Min(maxOutChunk, output.Length - outPos));

      var res = decoder.Decode(inChunk, outChunk, out int consumed, out int written);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == LzmaAloneDecodeResult.Finished)
      {
        Assert.Equal(output.Length, outPos);
        break;
      }

      if (res == LzmaAloneDecodeResult.InvalidData)
        throw new InvalidOperationException("Получили InvalidData на корректном тестовом потоке.");

      if (res == LzmaAloneDecodeResult.NotSupported)
        throw new InvalidOperationException("Получили NotSupported на корректном тестовом потоке.");

      // Если ввод закончился, а декодер просит ещё — это ошибка (мы строим корректный поток).
      if (res == LzmaAloneDecodeResult.NeedMoreInput && inPos == encoded.Length)
        throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");

      // Если вывод закончился, а декодер всё ещё не завершился — это ошибка.
      if (res == LzmaAloneDecodeResult.NeedMoreOutput && outPos == output.Length)
        throw new InvalidOperationException("Выход закончился, но декодер не завершился.");

      if (inPos == encoded.Length && outPos == output.Length)
        throw new InvalidOperationException("Декодер не завершился, хотя ввод закончился и место под вывод кончилось.");
    }

    return output;
  }
}
