using System.Text;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2LzmaEncoderTests
{
  [Fact]
  public void EncodeDecode_LiteralOnly_РаботаетЗаОдинВызов()
  {
    byte[] data = Encoding.ASCII.GetBytes("Hello LZMA2 (literal-only)!");

    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnly(data, props, dict, out _);

    byte[] decoded = DecodeAllOneShot(encoded, data.Length, dict);

    Assert.Equal(data, decoded);
  }

  [Fact]
  public void EncodeDecode_Script_ЛитералыПлюсMatch_ДаетТочныйРезультат()
  {
    // ABC + match(distance=3,len=3) => ABCABC, затем 'D' => ABCABCD
    var script = new[]
    {
      LzmaEncodeOp.Lit((byte)'A'),
      LzmaEncodeOp.Lit((byte)'B'),
      LzmaEncodeOp.Lit((byte)'C'),
      LzmaEncodeOp.Match(distance: 3, length: 3),
      LzmaEncodeOp.Lit((byte)'D'),
    };

    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte[] encoded = Lzma2LzmaEncoder.EncodeScript(script, props, dict, out _);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: 7, dictionarySize: dict, maxInChunk: 2, maxOutChunk: 3);

    Assert.Equal(Encoding.ASCII.GetBytes("ABCABCD"), decoded);
  }

  [Fact]
  public void EncodeLiteralOnlyChunked_ПустойВход_ВозвращаетТолькоEndMarker()
  {
    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
      ReadOnlySpan<byte>.Empty,
      props,
      dict,
      maxUnpackChunkSize: 16,
      out _);

    Assert.Equal([0x00], encoded);
  }

  [Fact]
  public void EncodeDecode_LiteralOnlyChunked_НесколькоЧанков_ДаетТочныйРезультат()
  {
    // Делаем вход заметно больше, чем maxUnpackChunkSize, чтобы гарантированно получилось несколько чанков.
    byte[] data = new byte[200];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)((i * 31 + 7) & 0xFF);

    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
      data,
      props,
      dict,
      maxUnpackChunkSize: 50,
      out _);

    // В конце должен быть end-marker.
    Assert.Equal((byte)0x00, encoded[^1]);

    // Проверяем корректность декодирования (и заодно потоковую подачу).
    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: data.Length, dictionarySize: dict, maxInChunk: 3, maxOutChunk: 7);

    Assert.Equal(data, decoded);
  }

  [Fact]
  public void EncodeLiteralOnlyChunked_СвойстваПишутсяТолькоВПервомЧанке()
  {
    // Делаем так, чтобы гарантированно получилось несколько чанков.
    byte[] data = new byte[100];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)i;

    var props = new LzmaProperties(3, 0, 2);

    byte propsByte;
    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
      data,
      props,
      dictionarySize: 1 << 20,
      maxUnpackChunkSize: 16,
      out propsByte);

    Assert.Equal(props.ToByteOrThrow(), propsByte);

    int offset = 0;
    int chunkIndex = 0;

    while (true)
    {
      var headerRes = Lzma2ChunkHeader.TryRead(encoded.AsSpan(offset), out var header, out int headerBytes);
      Assert.Equal(Lzma2ReadHeaderResult.Ok, headerRes);

      if (header.Kind == Lzma2ChunkKind.End)
        break;

      if (chunkIndex == 0)
      {
        Assert.True(header.HasProperties);
        Assert.True(header.ResetDictionary);
      }
      else
      {
        Assert.False(header.HasProperties);
        Assert.False(header.ResetDictionary);
      }

      Assert.True(header.ResetState);

      offset += headerBytes + header.PayloadSize;
      chunkIndex++;
    }

    Assert.True(chunkIndex >= 2);
  }

  [Fact]
  public void EncodeLiteralOnlyChunkedAuto_МожетВыбратьCopyЕслиТакКороче()
  {
    // На очень маленьких данных LZMA почти всегда проигрывает по размеру из‑за накладных расходов.
    byte[] data = new byte[20];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)i;

    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte propsByte;
    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunkedAuto(
      data,
      props,
      dict,
      maxUnpackChunkSize: 32,
      out propsByte);

    Assert.Equal(props.ToByteOrThrow(), propsByte);

    // Первый чанк должен быть COPY (reset dictionary).
    var headerRes = Lzma2ChunkHeader.TryRead(encoded, out var header, out _);
    Assert.Equal(Lzma2ReadHeaderResult.Ok, headerRes);
    Assert.Equal(Lzma2ChunkKind.Copy, header.Kind);
    Assert.True(header.ResetDictionary);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: data.Length, dictionarySize: dict, maxInChunk: 3, maxOutChunk: 5);
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void EncodeLiteralOnlyChunkedAuto_МожетВыбратьLzmaЕслиТакКороче()
  {
    // Повторяющиеся байты хорошо кодируются даже literal-only режимом.
    byte[] data = new byte[512];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)0x41;

    var props = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    const int dict = 1 << 20;

    byte propsByte;
    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunkedAuto(
      data,
      props,
      dict,
      maxUnpackChunkSize: 128,
      out propsByte);

    Assert.Equal(props.ToByteOrThrow(), propsByte);

    // Первый чанк должен быть LZMA (и с properties).
    var headerRes = Lzma2ChunkHeader.TryRead(encoded, out var header0, out _);
    Assert.Equal(Lzma2ReadHeaderResult.Ok, headerRes);
    Assert.Equal(Lzma2ChunkKind.Lzma, header0.Kind);
    Assert.True(header0.ResetDictionary);
    Assert.True(header0.HasProperties);

    // В stream должен быть ровно один LZMA-чанк с properties.
    int propsChunks = 0;
    int offset = 0;

    while (true)
    {
      var res = Lzma2ChunkHeader.TryRead(encoded.AsSpan(offset), out var header, out int headerBytes);
      Assert.Equal(Lzma2ReadHeaderResult.Ok, res);

      if (header.Kind == Lzma2ChunkKind.End)
        break;

      if (header.Kind == Lzma2ChunkKind.Lzma && header.HasProperties)
        propsChunks++;

      offset += headerBytes + header.PayloadSize;
    }

    Assert.Equal(1, propsChunks);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: data.Length, dictionarySize: dict, maxInChunk: 7, maxOutChunk: 11);
    Assert.Equal(data, decoded);
  }

  [Fact]
  public void EncodeLiteralOnlyChunkedAuto_CopyПотомLzma_ПервыйLzmaЧанкБезResetDictionary()
  {
    // Делаем 2 чанка по 32 байта:
    // - первый: почти "случайные" (чтобы COPY выиграл)
    // - второй: нули (чтобы LZMA выиграл)
    byte[] data = new byte[64];

    for (int i = 0; i < 32; i++)
      data[i] = (byte)((i * 13 + 7) & 0xFF);

    // data[32..] уже заполнен нулями.

    var props = new LzmaProperties(3, 0, 2);
    const int dict = 1 << 20;

    byte[] encoded = Lzma2LzmaEncoder.EncodeLiteralOnlyChunkedAuto(
      data,
      props,
      dict,
      maxUnpackChunkSize: 32,
      out byte propsByte);

    Assert.Equal(props.ToByteOrThrow(), propsByte);

    int offset = 0;

    var res0 = Lzma2ChunkHeader.TryRead(encoded.AsSpan(offset), out var h0, out int hb0);
    Assert.Equal(Lzma2ReadHeaderResult.Ok, res0);
    Assert.Equal(Lzma2ChunkKind.Copy, h0.Kind);
    Assert.True(h0.ResetDictionary);
    offset += hb0 + h0.PayloadSize;

    var res1 = Lzma2ChunkHeader.TryRead(encoded.AsSpan(offset), out var h1, out int hb1);
    Assert.Equal(Lzma2ReadHeaderResult.Ok, res1);
    Assert.Equal(Lzma2ChunkKind.Lzma, h1.Kind);

    // Это первый LZMA-чанк: properties должны быть, но dictionary сбрасывать нельзя (до него был COPY-чанк).
    Assert.True(h1.HasProperties);
    Assert.False(h1.ResetDictionary);
    Assert.True(h1.ResetState);

    // Для LZMA2 это диапазон control base 0xC0..0xDF (props + reset state, без reset dictionary).
    Assert.InRange(h1.Control, (byte)0xC0, (byte)0xDF);

    offset += hb1 + h1.PayloadSize;

    var res2 = Lzma2ChunkHeader.TryRead(encoded.AsSpan(offset), out var h2, out _);
    Assert.Equal(Lzma2ReadHeaderResult.Ok, res2);
    Assert.Equal(Lzma2ChunkKind.End, h2.Kind);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: data.Length, dictionarySize: dict, maxInChunk: 3, maxOutChunk: 7);
    Assert.Equal(data, decoded);
  }

  private static byte[] DecodeAllOneShot(byte[] encoded, int expectedOutputSize, int dictionarySize)
  {
    var decoder = new Lzma2IncrementalDecoder(progress: null, dictionarySize: dictionarySize);

    byte[] output = new byte[expectedOutputSize];

    Lzma2DecodeResult res = decoder.Decode(encoded, output, out int consumed, out int written);

    Assert.Equal(expectedOutputSize, written);
    Assert.True(consumed <= encoded.Length);

    // Может быть как Finished, так и NeedMoreInput (если мы не докормили end-marker).
    // В one-shot здесь должен быть Finished, потому что encoded содержит end-marker.
    Assert.Equal(Lzma2DecodeResult.Finished, res);

    return output;
  }

  private static byte[] DecodeAllStreamed(byte[] encoded, int expectedOutputSize, int dictionarySize, int maxInChunk, int maxOutChunk)
  {
    var decoder = new Lzma2IncrementalDecoder(progress: null, dictionarySize: dictionarySize);

    byte[] output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    // Сначала получаем ровно expectedOutputSize байт выходных данных.
    while (outPos < output.Length)
    {
      int inTake = Math.Min(maxInChunk, encoded.Length - inPos);
      ReadOnlySpan<byte> inChunk = encoded.AsSpan(inPos, inTake);

      int outTake = Math.Min(maxOutChunk, output.Length - outPos);
      Span<byte> outChunk = output.AsSpan(outPos, outTake);

      Lzma2DecodeResult res = decoder.Decode(inChunk, outChunk, out int consumed, out int written);

      // Декодер обязан делать прогресс, иначе зависнем.
      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      // В конце, когда выход заполнен, допускаем NeedMoreInput (дожевать end-marker будем ниже).
      if (res == Lzma2DecodeResult.InvalidData || res == Lzma2DecodeResult.NotSupported)
        throw new InvalidOperationException($"Неожиданный результат декодирования: {res}.");
    }

    // Теперь можно "дожевать" оставшийся вход (например end-marker) уже без выхода.
    Span<byte> emptyOut = Span<byte>.Empty;
    while (inPos < encoded.Length)
    {
      int inTake = Math.Min(maxInChunk, encoded.Length - inPos);
      ReadOnlySpan<byte> inChunk = encoded.AsSpan(inPos, inTake);

      Lzma2DecodeResult res = decoder.Decode(inChunk, emptyOut, out int consumed, out int written);

      if (consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся при дожёвывании хвоста входа.");

      inPos += consumed;

      if (res == Lzma2DecodeResult.Finished)
        break;
      if (res == Lzma2DecodeResult.InvalidData || res == Lzma2DecodeResult.NotSupported)
        throw new InvalidOperationException($"Неожиданный результат декодирования: {res}.");
    }

    return output;
  }
}
