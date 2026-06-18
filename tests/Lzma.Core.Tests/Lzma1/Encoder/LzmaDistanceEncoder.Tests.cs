using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDistanceEncoderTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  public void EncodeDecode_РазныеДистанции_ДаютТочныйРезультат(int lenToPosState)
  {
    // Набор «интересных» расстояний:
    // - маленькие (posSlot < 4)
    // - средние (posSlot 4..13)
    // - большие (posSlot >= 14) -> directBits + align
    uint[] distances =
    [
      1,
      2,
      3,
      4,
      5,
      6,
      7,
      8,
      9,
      15,
      16,
      31,
      32,
      63,
      64,
      127,
      128,
      129,
      130,
      200,
      255,
      256,
      257,
      511,
      512,
      1024,
    ];

    var enc = new LzmaDistanceEncoder();
    var encRange = new LzmaRangeEncoder();

    var dec = new LzmaDistanceDecoder();
    var decRange = new LzmaRangeDecoder();

    foreach (uint distance in distances)
    {
      // 1) Кодируем distance.
      enc.Reset();
      encRange.Reset();
      enc.EncodeDistance(encRange, lenToPosState, distance);
      encRange.Flush();
      byte[] bytes = encRange.ToArray();

      // 2) Декодируем обратно.
      dec.Reset();
      decRange.Reset();

      int offset = 0;
      var initRes = decRange.TryInitialize(bytes, ref offset);
      Assert.Equal(LzmaRangeInitResult.Ok, initRes);

      var res = dec.TryDecodeDistance(ref decRange, bytes, ref offset, lenToPosState, out uint decoded);
      Assert.Equal(LzmaRangeDecodeResult.Ok, res);
      Assert.Equal(distance, decoded);
    }
  }

  [Fact]
  public void EncodeDistance_ЕслиDistanceРавен0_БросаетИсключение()
  {
    var enc = new LzmaDistanceEncoder();
    var range = new LzmaRangeEncoder();
    Assert.Throws<ArgumentOutOfRangeException>(() => enc.EncodeDistance(range, lenToPosState: 0, distance: 0));
  }
}
