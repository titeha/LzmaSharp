using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2IncrementalDecoderFromPropertiesTests
{
  [Fact]
  public void Decode_CopyChunk_Works_When_Decoder_Is_Created_From_Lzma2PropertiesByte()
  {
    const byte lzma2PropertiesByte = 0; // минимальный словарь (4 KiB)
    var decoder = new Lzma2IncrementalDecoder(lzma2PropertiesByte);

    byte[] payload = [(byte)'A', (byte)'B', (byte)'C'];
    byte[] input = BuildCopyChunkThenEnd(payload, resetDictionary: true);

    // Делаем выход чуть больше, чтобы декодер смог дочитать End marker в том же вызове.
    byte[] output = new byte[payload.Length + 1];

    var res = decoder.Decode(input, output, out int bytesConsumed, out int bytesWritten);

    Assert.Equal(Lzma2DecodeResult.Finished, res);
    Assert.Equal(input.Length, bytesConsumed);
    Assert.Equal(payload.Length, bytesWritten);
    Assert.Equal(payload, output.AsSpan(0, payload.Length).ToArray());
  }

  [Fact]
  public void Ctor_Throws_For_Invalid_Lzma2PropertiesByte()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new Lzma2IncrementalDecoder((byte)41));
  }

  [Fact]
  public void Ctor_Throws_When_DictionarySize_TooLarge_For_Int()
  {
    Assert.Throws<ArgumentOutOfRangeException>(() => new Lzma2IncrementalDecoder((byte)40));
  }

  private static byte[] BuildCopyChunkThenEnd(byte[] payload, bool resetDictionary)
  {
    ArgumentNullException.ThrowIfNull(payload);

    // В LZMA2 размер copy-чанка хранится как (N - 1), поэтому N не может быть нулём.
    if (payload.Length == 0)
      throw new ArgumentException("Payload должен быть непустым.", nameof(payload));

    // Copy-чанк хранит длину в 16 бит (unpackSize-1).
    if (payload.Length > 0x10000)
      throw new ArgumentOutOfRangeException(nameof(payload), "В этом тесте ожидается payload <= 65536 байт.");

    byte control = resetDictionary ? (byte)0x01 : (byte)0x02;

    int sizeMinus1 = payload.Length - 1;
    byte hi = (byte)(sizeMinus1 >> 8);
    byte lo = (byte)(sizeMinus1 & 0xFF);

    byte[] input = new byte[1 + 2 + payload.Length + 1];
    input[0] = control;
    input[1] = hi;
    input[2] = lo;

    Buffer.BlockCopy(payload, 0, input, 3, payload.Length);

    input[^1] = 0x00; // End marker
    return input;
  }
}
