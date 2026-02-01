using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipPackInfoReaderTests
{
  [Fact]
  public void TryRead_МинимальныйPackInfo_ОдинПоток_Работает()
  {
    // PackInfo ::= kPackInfo packPos numPackStreams kSize size kEnd
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x0A, // size[0] = 10
      SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out SevenZipPackInfo packInfo, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);
    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Single(packInfo.PackSizes);
    Assert.Equal(10UL, packInfo.PackSizes[0]);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиОбрезано()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      // дальше обрываем
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиПервыйБайтНеPackInfo()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиВстречаетсяCRC()
  {
    // CRC-блок пока сознательно не поддерживаем.
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x01, // size[0] = 1
      SevenZipNid.Crc,
      SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }
}
