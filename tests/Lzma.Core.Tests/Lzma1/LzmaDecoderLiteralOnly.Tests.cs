using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

/// <summary>
/// Тесты декодера, которые используют ТОЛЬКО литералы (без match и без rep).
/// Это важно: мы хотим иметь стабильную базу, которую легко отлаживать.
/// </summary>
public sealed class LzmaDecoderLiteralOnlyTests
{
  [Fact]
  public void Decode_OneShot_Produces_Exact_Output()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte[] original = "Hello, LZMA!"u8.ToArray();
    byte[] compressed = LzmaTestLiteralOnlyEncoder.Encode(props, original);

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 16);
    byte[] dst = new byte[original.Length];

    var res = decoder.Decode(compressed, out int consumed, dst, out int written, out var progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(original.Length, written);
    Assert.Equal(original, dst);

    // Прогресс должен отражать то, сколько реально было потреблено/записано.
    Assert.Equal(consumed, progress.BytesRead);
    Assert.Equal(written, progress.BytesWritten);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    byte[] original = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
    byte[] compressed = LzmaTestLiteralOnlyEncoder.Encode(props, original);

    var decoder = new LzmaDecoder(props, dictionarySize: 1 << 16);
    var output = new List<byte>(original.Length);

    // Важно: stackalloc внутри цикла потенциально может привести к переполнению стека,
    // поэтому буфер выделяем ОДИН РАЗ снаружи.
    Span<byte> outBuffer = stackalloc byte[7];

    int srcOffset = 0;
    while (output.Count < original.Length)
    {
      // Ограничиваем выходной буфер оставшимся количеством байт.
      // Иначе в самом конце теста декодер может корректно запросить NeedsMoreInput
      // (пытаясь продолжить декодирование), хотя нам уже достаточно данных.
      int remaining = original.Length - output.Count;
      Span<byte> outChunk = outBuffer.Slice(0, Math.Min(outBuffer.Length, remaining));

      // Дробим и вход, и выход.
      int inChunkSize = Math.Min(3, compressed.Length - srcOffset);
      ReadOnlySpan<byte> inChunk = compressed.AsSpan(srcOffset, inChunkSize);

      var res = decoder.Decode(inChunk, out int consumed, outChunk, out int written, out _);
      srcOffset += consumed;
      output.AddRange(outChunk.Slice(0, written).ToArray());

      if (res == LzmaDecodeResult.NeedsMoreInput)
      {
        if (srcOffset >= compressed.Length)
          throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");
        continue;
      }

      if (res != LzmaDecodeResult.Ok)
        throw new InvalidOperationException($"Неожиданный результат декодера: {res}");
    }

    Assert.Equal(original, output.ToArray());
  }
}
