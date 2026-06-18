using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public class Lzma2BlockHeaderTests
{
  [Fact]
  public void Parse_EndOfStream()
  {
    var buffer = new byte[] { 0x00 };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(1, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.EndOfStream, header.Type);
  }

  [Fact]
  public void Parse_UncompressedResetDic()
  {
    // Control=0x01, UnpackSize=0x0203 → 0x0203 + 1 = 516
    var buffer = new byte[] { 0x01, 0x02, 0x03 };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(3, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.UncompressedResetDic, header.Type);
    Assert.Equal(516u, header.UnpackSize);
    Assert.Equal(516u, header.PackSize);
  }

  [Fact]
  public void Parse_UncompressedNoReset()
  {
    // Control=0x02, UnpackSize=0x0405 → 0x0405 + 1 = 1030
    var buffer = new byte[] { 0x02, 0x04, 0x05 };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(3, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.UncompressedNoReset, header.Type);
    Assert.Equal(1030u, header.UnpackSize);
  }

  [Fact]
  public void Parse_LzmaNoReset()
  {
    // Control=0x80 (10000000), Unpack=0x000102 → 259, Pack=0x0003 → 4
    var buffer = new byte[] { 0x80, 0x01, 0x02, 0x00, 0x03 };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(5, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.LzmaNoReset, header.Type);
    Assert.Equal(259u, header.UnpackSize);
    Assert.Equal(4u, header.PackSize);
  }

  [Fact]
  public void Parse_LzmaResetState()
  {
    // Control=0xA0 (10100000) — LZMA + сброс состояния
    var buffer = new byte[] { 0xA0, 0x01, 0x02, 0x00, 0x03 };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(5, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.LzmaResetState, header.Type);
  }

  [Fact]
  public void Parse_LzmaResetStateAndProps()
  {
    // Control=0xC0 (11000000) — LZMA + сброс состояния + новые свойства
    var buffer = new byte[] { 0xC0, 0x01, 0x02, 0x00, 0x03, 0x5D };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(6, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.LzmaResetStateAndProps, header.Type);
    Assert.True(header.Props.HasValue);
    Assert.Equal((byte)0x5D, header.Props.Value);
  }

  [Fact]
  public void Parse_LzmaFullReset()
  {
    // Control=0xE0 (11100000) — полный сброс словаря
    var buffer = new byte[] { 0xE0, 0x01, 0x02, 0x00, 0x03, 0x5D };
    var result = Lzma2BlockHeader.TryParse(buffer, out var header);

    Assert.Equal(6, result);
    Assert.Equal(Lzma2BlockHeader.BlockType.LzmaFullReset, header.Type);
    Assert.True(header.Props.HasValue);
    Assert.Equal((byte)0x5D, header.Props.Value);
  }

  [Fact]
  public void Parse_InsufficientData_ReturnsZero()
  {
    var buffer = new byte[] { 0xC0, 0x01 }; // Недостаточно байт
    var result = Lzma2BlockHeader.TryParse(buffer, out _);

    Assert.Equal(0, result);
  }

  [Fact]
  public void Parse_InvalidControlByte_ReturnsZero()
  {
    var buffer = new byte[] { 0x03 }; // Недопустимый control byte
    var result = Lzma2BlockHeader.TryParse(buffer, out _);

    Assert.Equal(0, result);
  }
}
