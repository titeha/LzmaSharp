using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2PropertiesFromDictionarySizeTests
{
  [Fact]
  public void TryCreateFromDictionarySize_RoundTrip_ForAllValidProps()
  {
    for (byte prop = 0; prop <= Lzma2Properties.MaxDictionaryProp; prop++)
    {
      Assert.True(Lzma2Properties.TryParse(prop, out var parsed));

      Assert.True(Lzma2Properties.TryCreateFromDictionarySize(parsed.DictionarySize, out var created));
      Assert.Equal(prop, created.DictionaryProp);
      Assert.Equal(parsed.DictionarySize, created.DictionarySize);
    }
  }

  [Theory]
  [InlineData(0u)]
  [InlineData(1u)]
  [InlineData(4095u)]
  public void TryCreateFromDictionarySize_TooSmall_ReturnsFalse(uint dictionarySize)
  {
    Assert.False(Lzma2Properties.TryCreateFromDictionarySize(dictionarySize, out _));
  }

  [Fact]
  public void TryCreateFromDictionarySize_RoundsUp_ToNextSupportedValue()
  {
    // Между prop=0 (4 KiB) и prop=1 (6 KiB).
    Assert.True(Lzma2Properties.TryCreateFromDictionarySize(5000u, out var created));

    Assert.Equal((byte)1, created.DictionaryProp);
    Assert.Equal(6u * 1024u, created.DictionarySize);
  }
}
