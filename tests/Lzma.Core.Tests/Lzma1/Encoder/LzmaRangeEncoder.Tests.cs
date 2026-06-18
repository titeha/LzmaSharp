using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaRangeEncoderTests
{
  [Fact]
  public void Flush_Produces_At_Least_5_Bytes()
  {
    var enc = new LzmaRangeEncoder();
    enc.Flush();
    var bytes = enc.ToArray();

    // Декодер при инициализации читает 5 байт Code.
    Assert.True(bytes.Length >= 5);
  }

  [Fact]
  public void Encoded_Bits_Can_Be_Decoded_By_RangeDecoder()
  {
    // Генерируем детерминированную последовательность битов.
    var rng = new Random(12345);
    const int count = 1_000;

    // Кодируем биты.
    var enc = new LzmaRangeEncoder();
    ushort probEnc = LzmaConstants.ProbabilityInitValue;

    var expected = new uint[count];
    for (int i = 0; i < count; i++)
    {
      uint bit = (uint)rng.Next(0, 2);
      expected[i] = bit;
      enc.EncodeBit(ref probEnc, bit);
    }

    // Завершаем поток (дописываем байты, которые «застряли» в low/cache).
    enc.Flush();
    var encoded = enc.ToArray();

    // Теперь декодируем тем же RangeDecoder и убеждаемся, что получаем те же биты.
    var dec = new LzmaRangeDecoder();
    dec.Reset();

    int offset = 0;
    var initRes = dec.TryInitialize(encoded, ref offset);
    Assert.Equal(LzmaRangeInitResult.Ok, initRes);

    ushort probDec = LzmaConstants.ProbabilityInitValue;
    for (int i = 0; i < count; i++)
    {
      var bitRes = dec.TryDecodeBit(ref probDec, encoded, ref offset, out uint actual);
      Assert.Equal(LzmaRangeDecodeResult.Ok, bitRes);
      Assert.Equal(expected[i], actual);
    }
  }
}
