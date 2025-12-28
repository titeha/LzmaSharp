// Шаг 1: тестируем только разбор заголовков чанков LZMA2.
// Никакой распаковки здесь нет — проверяем только корректное чтение полей.

using Lzma.Core.Lzma2;

namespace Lzma.Core.Tests.Lzma2;

public sealed class Lzma2ChunkHeaderTests
{
  [Fact]
  public void EndMarker_Control00_Parses()
  {
    byte[] data = [0x00];

    var res = Lzma2ChunkHeader.TryRead(data, out var header, out int consumed);

    Assert.Equal(Lzma2ReadHeaderResult.Ok, res);
    Assert.Equal(1, consumed);
    Assert.Equal(Lzma2ChunkKind.End, header.Kind);
    Assert.Equal(0x00, header.Control);
    Assert.Equal(0, header.UnpackSize);
    Assert.Equal(0, header.PackSize);
    Assert.Equal(0, header.PayloadSize);
    Assert.Equal(1, header.TotalSize);
  }

  [Fact]
  public void CopyChunk_ResetDic_Control01_Parses()
  {
    // control=0x01, unpackSizeMinus1=0x0000 => unpackSize=1
    byte[] data = [0x01, 0x00, 0x00];

    var res = Lzma2ChunkHeader.TryRead(data, out var header, out int consumed);

    Assert.Equal(Lzma2ReadHeaderResult.Ok, res);
    Assert.Equal(3, consumed);
    Assert.Equal(Lzma2ChunkKind.Copy, header.Kind);
    Assert.True(header.ResetDictionary);
    Assert.False(header.ResetState);
    Assert.False(header.HasProperties);
    Assert.Equal(1, header.UnpackSize);
    Assert.Equal(0, header.PackSize);
    Assert.Equal(1, header.PayloadSize);
    Assert.Equal(3 + 1, header.TotalSize);
  }

  [Fact]
  public void CopyChunk_NoResetDic_Control02_Parses()
  {
    // unpackSizeMinus1=0x0010 => unpackSize=17
    byte[] data = [0x02, 0x00, 0x10];

    var res = Lzma2ChunkHeader.TryRead(data, out var header, out int consumed);

    Assert.Equal(Lzma2ReadHeaderResult.Ok, res);
    Assert.Equal(3, consumed);
    Assert.Equal(Lzma2ChunkKind.Copy, header.Kind);
    Assert.False(header.ResetDictionary);
    Assert.Equal(17, header.UnpackSize);
    Assert.Equal(17, header.PayloadSize);
    Assert.Equal(3 + 17, header.TotalSize);
  }

  [Fact]
  public void LzmaChunk_NoProps_Control80_Parses()
  {
    // control=0x80 => unpackSizeMinus1_21 = 0x000000, packSizeMinus1=0x0000
    // => unpackSize=1, packSize=1
    byte[] data = [0x80, 0x00, 0x00, 0x00, 0x00];

    var res = Lzma2ChunkHeader.TryRead(data, out var header, out int consumed);

    Assert.Equal(Lzma2ReadHeaderResult.Ok, res);
    Assert.Equal(5, consumed);
    Assert.Equal(Lzma2ChunkKind.Lzma, header.Kind);
    Assert.Equal(0x80, header.Control);
    Assert.True(header.ResetDictionary); // 0x80..0x9F
    Assert.False(header.ResetState);     // 0x80..0xBF
    Assert.False(header.HasProperties);
    Assert.Equal(1, header.UnpackSize);
    Assert.Equal(1, header.PackSize);
    Assert.Equal(1, header.PayloadSize);
    Assert.Equal(5 + 1, header.TotalSize);
    Assert.Null(header.Properties);
  }

  [Fact]
  public void LzmaChunk_WithProps_ControlE0_Parses()
  {
    // Пример похожий на твой (E0 00 54 ... props)
    // unpackSizeMinus1_21 = (E0 & 1F)<<16 + 0x0054 = 0x000054 => unpackSize=85
    // packSizeMinus1 = 0x0010 => packSize=17
    byte[] data = [0xE0, 0x00, 0x54, 0x00, 0x10, 0x5D];

    var res = Lzma2ChunkHeader.TryRead(data, out var header, out int consumed);

    Assert.Equal(Lzma2ReadHeaderResult.Ok, res);
    Assert.Equal(6, consumed);
    Assert.Equal(Lzma2ChunkKind.Lzma, header.Kind);
    Assert.False(header.ResetDictionary); // E0 >= A0
    Assert.True(header.ResetState);       // E0 >= C0
    Assert.True(header.HasProperties);    // E0 >= E0
    Assert.Equal(85, header.UnpackSize);
    Assert.Equal(17, header.PackSize);
    Assert.Equal(17, header.PayloadSize);
    Assert.Equal(6 + 17, header.TotalSize);
    Assert.Equal((byte)0x5D, header.Properties!);
  }

  [Fact]
  public void InvalidControl_03_ReturnsInvalidData()
  {
    byte[] data = [0x03, 0x00, 0x00];

    var res = Lzma2ChunkHeader.TryRead(data, out _, out _);

    Assert.Equal(Lzma2ReadHeaderResult.InvalidData, res);
  }

  [Fact]
  public void TruncatedHeader_ReturnsNeedMoreInput()
  {
    // Copy header требует 3 байта, а мы дали 2
    byte[] data = [0x01, 0x00];

    var res = Lzma2ChunkHeader.TryRead(data, out _, out _);

    Assert.Equal(Lzma2ReadHeaderResult.NeedMoreInput, res);
  }
}
