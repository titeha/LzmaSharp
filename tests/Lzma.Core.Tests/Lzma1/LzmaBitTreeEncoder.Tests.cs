using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaBitTreeEncoderTests
{
  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(5)]
  public void EncodeDecodeSymbol_КруговойПрогон_ДаётТочныйСимвол(int numBits)
  {
    int symbolCount = 1 << numBits;

    for (uint symbol = 0; symbol < (uint)symbolCount; symbol++)
    {
      var rangeEnc = new LzmaRangeEncoder();
      rangeEnc.Reset();

      var treeEnc = new LzmaBitTreeEncoder(numBits);
      treeEnc.Reset();

      treeEnc.EncodeSymbol(rangeEnc, symbol);
      rangeEnc.Flush();
      byte[] encoded = rangeEnc.ToArray();

      var rangeDec = new LzmaRangeDecoder();
      int offset = 0;
      Assert.Equal(LzmaRangeInitResult.Ok, rangeDec.TryInitialize(encoded, ref offset));

      var treeDec = new LzmaBitTreeDecoder(numBits);
      treeDec.Reset();

      var res = treeDec.TryDecodeSymbol(ref rangeDec, encoded, ref offset, out uint decoded);
      Assert.Equal(LzmaRangeDecodeResult.Ok, res);
      Assert.Equal(symbol, decoded);
    }
  }

  [Theory]
  [InlineData(1)]
  [InlineData(2)]
  [InlineData(3)]
  [InlineData(5)]
  public void EncodeDecodeReverseSymbol_КруговойПрогон_ДаётТочныйСимвол(int numBits)
  {
    int symbolCount = 1 << numBits;

    for (uint symbol = 0; symbol < (uint)symbolCount; symbol++)
    {
      var rangeEnc = new LzmaRangeEncoder();
      rangeEnc.Reset();

      var treeEnc = new LzmaBitTreeEncoder(numBits);
      treeEnc.Reset();

      treeEnc.EncodeReverseSymbol(rangeEnc, symbol);
      rangeEnc.Flush();
      byte[] encoded = rangeEnc.ToArray();

      var rangeDec = new LzmaRangeDecoder();
      int offset = 0;
      Assert.Equal(LzmaRangeInitResult.Ok, rangeDec.TryInitialize(encoded, ref offset));

      var treeDec = new LzmaBitTreeDecoder(numBits);
      treeDec.Reset();

      var res = treeDec.TryReverseDecode(ref rangeDec, encoded, ref offset, out uint decoded);
      Assert.Equal(LzmaRangeDecodeResult.Ok, res);
      Assert.Equal(symbol, decoded);
    }
  }

  [Fact]
  public void EncodeSymbol_БросаетИсключение_ЕслиСимволВнеДиапазона()
  {
    var rangeEnc = new LzmaRangeEncoder();
    rangeEnc.Reset();

    var treeEnc = new LzmaBitTreeEncoder(numBits: 3);

    Assert.Throws<ArgumentOutOfRangeException>(() => treeEnc.EncodeSymbol(rangeEnc, symbol: 8));
    Assert.Throws<ArgumentOutOfRangeException>(() => treeEnc.EncodeReverseSymbol(rangeEnc, symbol: 999));
  }
}
