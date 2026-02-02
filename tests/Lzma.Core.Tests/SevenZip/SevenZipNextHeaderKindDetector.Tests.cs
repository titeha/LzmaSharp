using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipNextHeaderKindDetectorTests
{
  [Fact]
  public void TryDetect_ПустойВход_NeedMoreInput()
  {
    var res = SevenZipNextHeaderKindDetector.TryDetect([], out _);
    Assert.Equal(SevenZipNextHeaderKindDetectResult.NeedMoreInput, res);
  }

  [Fact]
  public void TryDetect_Header_Ok()
  {
    var res = SevenZipNextHeaderKindDetector.TryDetect([0x01], out var kind);

    Assert.Equal(SevenZipNextHeaderKindDetectResult.Ok, res);
    Assert.Equal(SevenZipNextHeaderKind.Header, kind);
  }

  [Fact]
  public void TryDetect_EncodedHeader_Ok()
  {
    var res = SevenZipNextHeaderKindDetector.TryDetect([0x17], out var kind);

    Assert.Equal(SevenZipNextHeaderKindDetectResult.Ok, res);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, kind);
  }

  [Fact]
  public void TryDetect_НезнакомыйБайт_InvalidData()
  {
    var res = SevenZipNextHeaderKindDetector.TryDetect([0x02], out _);
    Assert.Equal(SevenZipNextHeaderKindDetectResult.InvalidData, res);
  }
}
