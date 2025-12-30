using Xunit;
using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaPropertiesTests
{
  [Fact]
  public void TryParse_InvalidByte_ReturnsFalse()
  {
    // 225..255 — недопустимы.
    for (int b = 225; b <= 255; b++)
    {
      Assert.False(LzmaProperties.TryParse((byte)b, out _));
    }
  }

  [Fact]
  public void RoundTrip_AllValidCombinations_Work()
  {
    // Полный перебор всех валидных комбинаций.
    // Это дешёвый тест (225 комбинаций) и он отлично ловит ошибки в формулах.

    for (int pb = 0; pb <= LzmaProperties.MaxPb; pb++)
      for (int lp = 0; lp <= LzmaProperties.MaxLp; lp++)
        for (int lc = 0; lc <= LzmaProperties.MaxLc; lc++)
        {
          Assert.True(LzmaProperties.TryCreate(lc, lp, pb, out var props));

          byte b = props.ToByteOrThrow();

          Assert.True(LzmaProperties.TryParse(b, out var parsed));
          Assert.Equal(props, parsed);
        }
  }

  [Theory]
  [InlineData(-1, 0, 0)]
  [InlineData(9, 0, 0)]
  [InlineData(0, -1, 0)]
  [InlineData(0, 5, 0)]
  [InlineData(0, 0, -1)]
  [InlineData(0, 0, 5)]
  public void TryCreate_InvalidRanges_ReturnsFalse(int lc, int lp, int pb)
  {
    Assert.False(LzmaProperties.TryCreate(lc, lp, pb, out _));
  }
}
