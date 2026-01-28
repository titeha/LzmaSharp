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
