using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipLzma2CoderTests
{
  [Theory]
  [InlineData((byte)0, 0x00001000u)]   // 4 KiB
  [InlineData((byte)1, 0x00001800u)]   // 6 KiB
  [InlineData((byte)2, 0x00002000u)]   // 8 KiB
  [InlineData((byte)39, 0xC0000000u)]  // 3 * 2^30
  [InlineData((byte)40, 0xFFFFFFFFu)]  // special
  public void TryDecodeDictionarySize_ValidProperties_ReturnsExpected(byte properties, uint expected)
  {
    Assert.True(SevenZipLzma2Coder.TryDecodeDictionarySize(properties, out uint actual));
    Assert.Equal(expected, actual);
  }

  [Theory]
  [InlineData((byte)41)]
  [InlineData((byte)255)]
  public void TryDecodeDictionarySize_InvalidProperties_ReturnsFalse(byte properties)
  {
    Assert.False(SevenZipLzma2Coder.TryDecodeDictionarySize(properties, out uint actual));
    Assert.Equal(0u, actual);
  }
}
