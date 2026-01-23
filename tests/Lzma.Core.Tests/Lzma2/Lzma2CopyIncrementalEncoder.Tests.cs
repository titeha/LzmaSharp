using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public class Lzma2CopyIncrementalEncoderTests
{
  [Fact]
  public void Encode_OneShot_СовпадаетС_ОбычнымLzma2CopyEncoder()
  {
    // Данные чуть больше 64 КБ, чтобы гарантированно получить несколько COPY-чанков.
    byte[] input = new byte[(1 << 16) + 1234];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)(i * 31 + 7);

    byte[] expected = Lzma2CopyEncoder.Encode(input);

    var enc = new Lzma2CopyIncrementalEncoder();

    // Буфер достаточного размера, чтобы всё закодировать одним вызовом (но кодер всё равно может
    // делать несколько шагов внутри вызова).
    byte[] output = new byte[expected.Length];

    int inPos = 0;
    int outPos = 0;

    while (true)
    {
      var res = enc.Encode(
        input.AsSpan(inPos),
        output.AsSpan(outPos),
        isFinal: true,
        out int consumed,
        out int written,
        out _);

      Assert.True(consumed > 0 || written > 0, "Энкодер не продвинулся: не потребил ввод и не записал вывод.");

      inPos += consumed;
      outPos += written;

      if (res == Lzma2EncodeResult.Finished)
        break;
    }

    Assert.Equal(input.Length, inPos);
    Assert.Equal(expected.Length, outPos);
    Assert.Equal(expected, output);
  }

  [Fact]
  public void Encode_И_Декод_РаботаютПриОченьМаленькихБуферах()
  {
    // «Почти 2 чанка», чтобы проверить границу 64К и переход на следующий чанк.
    byte[] original = new byte[(1 << 16) * 2 + 17];
    for (int i = 0; i < original.Length; i++)
      original[i] = (byte)(i ^ (i >> 3));

    // 1) Кодируем потоково маленьким output-буфером.
    var enc = new Lzma2CopyIncrementalEncoder();
    var encoded = new List<byte>(capacity: original.Length + 1024);

    // Чтобы не ловить предупреждение CA2014 (stackalloc в цикле),
    // используем небольшой переиспользуемый буфер.
    byte[] outChunkArr = new byte[11];

    int srcPos = 0;
    while (true)
    {
      // Имитируем «ввод кусочками».
      int take = Math.Min(37, original.Length - srcPos);
      ReadOnlySpan<byte> srcChunk = original.AsSpan(srcPos, take);
      // А output делаем ещё меньше.
      Span<byte> outChunk = outChunkArr;

      bool isFinal = srcPos + take >= original.Length;

      var res = enc.Encode(
        srcChunk,
        outChunk,
        isFinal,
        out int consumed,
        out int written,
        out _);

      Assert.True(consumed > 0 || written > 0, "Энкодер не продвинулся: не потребил ввод и не записал вывод.");

      // Важно: bytesConsumed относится к srcChunk, поэтому двигаем srcPos именно на consumed.
      srcPos += consumed;

      for (int i = 0; i < written; i++)
        encoded.Add(outChunk[i]);

      if (res == Lzma2EncodeResult.Finished)
        break;

      // Если энкодер просит больше output — просто продолжаем (в следующей итерации дадим новый outChunk).
      // Если просит input — в следующей итерации дадим следующий кусок.
    }

    Assert.True(srcPos == original.Length, "Энкодер не потребил весь исходный ввод.");

    // 2) Декодируем обратно, тоже маленькими буферами.
    byte[] decoded = DecodeLzma2ToArray([.. encoded], expectedSize: original.Length);

    Assert.Equal(original, decoded);
  }

  private static byte[] DecodeLzma2ToArray(byte[] encoded, int expectedSize)
  {
    var dec = new Lzma2IncrementalDecoder();

    byte[] output = new byte[expectedSize];

    int srcPos = 0;
    int dstPos = 0;

    while (true)
    {
      // Даём декодеру маленький output-буфер.
      int dstTake = Math.Min(19, output.Length - dstPos);
      Span<byte> dstChunk = output.AsSpan(dstPos, dstTake);

      var res = dec.Decode(encoded.AsSpan(srcPos), dstChunk, out int consumed, out int written);

      Assert.True(consumed > 0 || written > 0, "Декодер не продвинулся: не потребил ввод и не записал вывод.");

      srcPos += consumed;
      dstPos += written;

      if (res == Lzma2DecodeResult.Finished)
        break;

      if (res == Lzma2DecodeResult.NeedMoreInput && srcPos >= encoded.Length)
        throw new InvalidOperationException("Вход закончился раньше, чем декодер завершил поток.");

      if (res == Lzma2DecodeResult.InvalidData)
        throw new InvalidOperationException("Декодер вернул InvalidData.");
    }

    Assert.Equal(output.Length, dstPos);
    return output;
  }
}
