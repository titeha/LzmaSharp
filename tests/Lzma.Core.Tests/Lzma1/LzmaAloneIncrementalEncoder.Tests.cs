using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaAloneIncrementalEncoderTests
{
  [Fact]
  public void EncodeDecode_LiteralOnly_РаботаетПриПотоковойПодаче_КрошечнымиКусками()
  {
    // Данные, чтобы проверить разные байты и границы.
    byte[] input =
    [
      65, 66, 67, 0, 255,
      10, 11, 12, 13, 14,
      200, 201, 202, 203,
      1, 2, 3, 4, 5,
      65, 66, 67, 68, 69
    ];

    var props = new LzmaProperties(3, 0, 2);
    int dictionarySize = 1 << 16;

    var encoder = new LzmaAloneIncrementalEncoder(
      properties: props,
      dictionarySize: dictionarySize,
      uncompressedSize: input.Length);

    byte[] encoded = EncodeAllStreamed(
      encoder,
      input,
      maxInChunk: 3,
      maxOutChunk: 2);

    // Заголовок должен соответствовать параметрам энкодера.
    Assert.Equal(LzmaAloneHeader.ReadResult.Ok, LzmaAloneHeader.TryRead(encoded, out var header, out int headerBytes));
    Assert.Equal(LzmaAloneHeader.HeaderSize, headerBytes);
    Assert.Equal(props, header.Properties);
    Assert.Equal(dictionarySize, header.DictionarySize);
    Assert.Equal((ulong)input.Length, header.UncompressedSize!);

    var decoder = new LzmaAloneIncrementalDecoder();
    byte[] decoded = DecodeAllStreamed(
      decoder,
      encoded,
      expectedOutputSize: input.Length,
      maxInChunk: 2,
      maxOutChunk: 3);

    Assert.Equal(input, decoded);

    Assert.Equal(input.Length, encoder.TotalBytesRead);
    Assert.Equal(encoded.Length, encoder.TotalBytesWritten);
  }

  private static byte[] EncodeAllStreamed(
    LzmaAloneIncrementalEncoder encoder,
    ReadOnlySpan<byte> input,
    int maxInChunk,
    int maxOutChunk)
  {
    List<byte> output = [];
    int inputOffset = 0;

    while (true)
    {
      int remainingIn = input.Length - inputOffset;
      int takeIn = remainingIn > 0 ? Math.Min(maxInChunk, remainingIn) : 0;

      bool isFinal = inputOffset + takeIn == input.Length;
      ReadOnlySpan<byte> inChunk = input.Slice(inputOffset, takeIn);

      byte[] outBuf = new byte[maxOutChunk];
      var res = encoder.Encode(inChunk, isFinal, outBuf, out int bytesConsumed, out int bytesWritten);

      if (bytesConsumed == 0 && bytesWritten == 0)
        throw new InvalidOperationException("Энкодер не продвинулся: не потребил ввод и не записал вывод.");

      inputOffset += bytesConsumed;

      for (int i = 0; i < bytesWritten; i++)
        output.Add(outBuf[i]);

      if (res == LzmaAloneEncodeResult.Finished)
        return output.ToArray();
    }
  }

  private static byte[] DecodeAllStreamed(
    LzmaAloneIncrementalDecoder decoder,
    ReadOnlySpan<byte> encoded,
    int expectedOutputSize,
    int maxInChunk,
    int maxOutChunk)
  {
    byte[] output = new byte[expectedOutputSize];

    int inOffset = 0;
    int outOffset = 0;

    while (outOffset < expectedOutputSize)
    {
      int inLen = Math.Min(maxInChunk, encoded.Length - inOffset);
      ReadOnlySpan<byte> inChunk = encoded.Slice(inOffset, inLen);

      int outLen = Math.Min(maxOutChunk, expectedOutputSize - outOffset);
      Span<byte> outChunk = output.AsSpan(outOffset, outLen);

      var res = decoder.Decode(inChunk, outChunk, out int bytesConsumed, out int bytesWritten);

      if (bytesConsumed == 0 && bytesWritten == 0)
        throw new InvalidOperationException("Декодер не продвинулся: не потребил ввод и не записал вывод.");

      inOffset += bytesConsumed;
      outOffset += bytesWritten;

      if (res == LzmaAloneDecodeResult.NeedMoreInput && inOffset >= encoded.Length)
        throw new InvalidOperationException("Декодер запросил больше ввода, но ввод закончился.");
    }

    return output;
  }
}
