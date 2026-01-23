using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

/// <summary>
/// Небольшие тесты на минимальный COPY-энкодер.
/// Здесь мы проверяем два важных свойства:
/// 1) формат потока корректен (заголовки, end marker);
/// 2) поток можно гарантированно разжать нашим же Lzma2IncrementalDecoder.
/// </summary>
public sealed class Lzma2CopyEncoderTests
{
  [Fact]
  public void Encode_Empty_Produces_EndMarkerOnly_And_Roundtrips()
  {
    byte[] encoded = Lzma2CopyEncoder.Encode(ReadOnlySpan<byte>.Empty);

    Assert.Single(encoded);
    Assert.Equal(0x00, encoded[0]);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: 0);
    Assert.Empty(decoded);
  }

  [Fact]
  public void Encode_SmallBuffer_Produces_OneCopyChunk_And_Roundtrips()
  {
    byte[] input = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    byte[] encoded = Lzma2CopyEncoder.Encode(input);

    // 1 чанк: 3 байта заголовка + 10 байт payload + 1 байт end marker
    Assert.Equal(3 + input.Length + 1, encoded.Length);

    // control
    Assert.Equal(0x01, encoded[0]); // reset dictionary

    // sizeMinus1 (big-endian)
    Assert.Equal(0x00, encoded[1]);
    Assert.Equal(0x09, encoded[2]); // 10 - 1

    // payload
    Assert.True(encoded.AsSpan(3, input.Length).SequenceEqual(input));

    // end marker
    Assert.Equal(0x00, encoded[^1]);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: input.Length);
    Assert.True(decoded.AsSpan().SequenceEqual(input));
  }

  [Fact]
  public void Encode_Splits_Into_Multiple_Chunks_And_Roundtrips()
  {
    // Делаем размер чуть больше MaxChunkSize, чтобы гарантированно получить 2 COPY-чанка.
    const int len = Lzma2CopyEncoder.MaxChunkSize + 5;
    byte[] input = new byte[len];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)i;

    byte[] encoded = Lzma2CopyEncoder .Encode(input);

    // Для 2 чанков: вход + 3*2 заголовка + 1 end marker
    Assert.Equal(input.Length + (3 * 2) + 1, encoded.Length);

    // Первый чанк
    Assert.Equal(0x01, encoded[0]);
    Assert.Equal(0xFF, encoded[1]);
    Assert.Equal(0xFF, encoded[2]); // 65536 - 1 = 0xFFFF

    // Второй чанк начинается сразу после payload первого
    const int secondChunkHeader = 3 + Lzma2CopyEncoder.MaxChunkSize;
    Assert.Equal(0x02, encoded[secondChunkHeader + 0]); // без reset dictionary

    // sizeMinus1 для 5 байт => 4
    Assert.Equal(0x00, encoded[secondChunkHeader + 1]);
    Assert.Equal(0x04, encoded[secondChunkHeader + 2]);

    // end marker
    Assert.Equal(0x00, encoded[^1]);

    byte[] decoded = DecodeAllStreamed(encoded, expectedOutputSize: input.Length);
    Assert.True(decoded.AsSpan().SequenceEqual(input));
  }

  private static byte[] DecodeAllStreamed(byte[] encoded, int expectedOutputSize)
  {
    // COPY-режиму словарь по факту не нужен для match'ей, но декодер всё равно ведёт dictionary.
    // Берём небольшой словарь, чтобы тесты были лёгкие.
    var decoder = new Lzma2IncrementalDecoder(dictionarySize: 1 << 16);

    byte[] output = new byte[expectedOutputSize];

    int inPos = 0;
    int outPos = 0;

    // Специально «мучаем» декодер мелкими кусками, чтобы проверять потоковый контракт.
    const int inputChunk = 7;
    const int outputChunk = 11;

    while (true)
    {
      ReadOnlySpan<byte> inSlice = encoded.AsSpan(inPos, Math.Min(inputChunk, encoded.Length - inPos));
      Span<byte> outSlice = output.AsSpan(outPos, Math.Min(outputChunk, output.Length - outPos));

      var res = decoder.Decode(inSlice, outSlice, out int consumed, out int written);

      if (res != Lzma2DecodeResult.Finished && consumed == 0 && written == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == Lzma2DecodeResult.Finished)
      {
        Assert.Equal(encoded.Length, inPos);
        Assert.Equal(output.Length, outPos);
        return output;
      }

      if (res == Lzma2DecodeResult.NeedMoreInput)
      {
        if (inPos >= encoded.Length)
          throw new InvalidOperationException("Вход закончился раньше, чем декодер сообщил Finished.");
        continue;
      }

      if (res == Lzma2DecodeResult.NeedMoreOutput)
      {
        if (outPos >= output.Length)
          throw new InvalidOperationException("Выходной буфер закончился раньше, чем декодер сообщил Finished.");
        continue;
      }

      // Для COPY-потока других состояний быть не должно.
      throw new InvalidOperationException($"Неожиданный результат декодирования: {res}");
    }
  }
}
