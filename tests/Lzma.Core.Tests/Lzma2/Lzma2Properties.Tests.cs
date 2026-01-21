// Шаг 27: тестируем разбор "внешних" properties LZMA2 (1 байт, задающий размер словаря).
//
// Эти properties НЕ являются LZMA (lc/lp/pb) properties.
// В 7z LZMA2 coder properties — это отдельный байт в заголовке метода.

using Lzma.Core.Lzma2;

using Xunit;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2PropertiesTests
{
  [Theory]
  // Минимальные значения.
  [InlineData(0, 4096u)]
  [InlineData(1, 6144u)]
  [InlineData(2, 8192u)]
  [InlineData(3, 12288u)]
  // Несколько значений из середины диапазона.
  [InlineData(10, 131072u)]     // 128 KiB
  [InlineData(11, 196608u)]     // 192 KiB
  [InlineData(20, 4194304u)]    // 4 MiB
  [InlineData(21, 6291456u)]    // 6 MiB
  [InlineData(30, 134217728u)]  // 128 MiB
  // Верхние значения.
  [InlineData(37, 1610612736u)] // 1.5 GiB
  [InlineData(38, 2147483648u)] // 2 GiB
  [InlineData(39, 3221225472u)] // 3 GiB
  [InlineData(40, 0xFFFF_FFFFu)]
  public void TryParse_ValidProps_ComputeExpectedDictionarySize(byte prop, uint expectedSize)
  {
    Assert.True(Lzma2Properties.TryParse(prop, out var properties));
    Assert.Equal(prop, properties.DictionaryProp);
    Assert.Equal(expectedSize, properties.DictionarySize);
  }

  [Theory]
  [InlineData(41)]
  [InlineData(255)]
  public void TryParse_InvalidProps_ReturnsFalse(byte prop)
  {
    Assert.False(Lzma2Properties.TryParse(prop, out _));
  }

  [Fact]
  public void TryGetDictionarySizeInt32_ReturnsFalse_WhenSizeDoesNotFitInt32()
  {
    // prop=38 => 2 GiB, что на 1 больше int.MaxValue.
    Assert.True(Lzma2Properties.TryParse(38, out var p38));
    Assert.False(p38.TryGetDictionarySizeInt32(out _));

    // prop=40 => спец-значение 0xFFFF_FFFF.
    Assert.True(Lzma2Properties.TryParse(40, out var p40));
    Assert.False(p40.TryGetDictionarySizeInt32(out _));
  }

  [Fact]
  public void TryGetDictionarySizeInt32_ReturnsTrue_ForSmallEnoughDictionary()
  {
    Assert.True(Lzma2Properties.TryParse(0, out var p0));
    Assert.True(p0.TryGetDictionarySizeInt32(out int size0));
    Assert.Equal(4096, size0);

    Assert.True(Lzma2Properties.TryParse(37, out var p37));
    Assert.True(p37.TryGetDictionarySizeInt32(out int size37));
    Assert.Equal(1610612736, size37);
  }
}
