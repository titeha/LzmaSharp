using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaConstantsTests
{
  [Fact]
  public void RangeCoder_ОсновныеКонстанты_ИмеютОжидаемыеЗначения()
  {
    // Эти значения — «канонические» для LZMA range coder'а.
    Assert.Equal(11, LzmaConstants.NumBitModelTotalBits);
    Assert.Equal(2048, LzmaConstants.BitModelTotal);
    Assert.Equal(5, LzmaConstants.NumMoveBits);
    Assert.Equal(0x0100_0000u, LzmaConstants.RangeTopValue);
  }

  [Fact]
  public void Lzma_Длины_ИмеютОжидаемыеСвязи()
  {
    // Минимальная длина совпадения в LZMA.
    Assert.Equal(2, LzmaConstants.MatchMinLen);

    // Максимальная длина должна быть > минимальной.
    Assert.True(LzmaConstants.MatchMaxLen > LzmaConstants.MatchMinLen);

    // «Каноническое» значение для LZMA: 273.
    Assert.Equal(273, LzmaConstants.MatchMaxLen);
  }
}
