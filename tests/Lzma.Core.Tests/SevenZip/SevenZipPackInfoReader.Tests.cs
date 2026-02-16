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
  public void TryRead_CrcAllAreDefined_ОдинПоток_Работает()
  {
    // PackInfo ::= kPackInfo packPos numPackStreams kSize size kCRC Digests kEnd
    byte[] data =
    [
        SevenZipNid.PackInfo,
        0x00,               // packPos = 0
        0x01,               // numPackStreams = 1
        SevenZipNid.Size,
        0x01,               // size[0] = 1

        SevenZipNid.Crc,
        0x01,               // AllAreDefined = 1
        0x44, 0x33, 0x22, 0x11, // CRC (1 шт)

        SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out SevenZipPackInfo packInfo, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);
    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Single(packInfo.PackSizes);
    Assert.Equal(1UL, packInfo.PackSizes[0]);
  }

  [Fact]
  public void TryRead_CrcPartialDefined_ДваПотока_Работает()
  {
    // 2 потока, CRC задан только для второго.
    // Defined bits: [false, true] => 0x40
    byte[] data =
    [
        SevenZipNid.PackInfo,
        0x00,               // packPos = 0
        0x02,               // numPackStreams = 2
        SevenZipNid.Size,
        0x01,               // size[0] = 1
        0x02,               // size[1] = 2

        SevenZipNid.Crc,
        0x00,               // AllAreDefined = 0
        0x40,               // Defined bitfield (1 байт)
        0x44, 0x33, 0x22, 0x11, // CRC (только 1 шт, для defined)

        SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out SevenZipPackInfo packInfo, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);
    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Equal(2, packInfo.PackSizes.Length);
    Assert.Equal(1UL, packInfo.PackSizes[0]);
    Assert.Equal(2UL, packInfo.PackSizes[1]);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиCrcAllAreDefinedНе0ИНе1()
  {
    byte[] data =
    [
        SevenZipNid.PackInfo,
        0x00,               // packPos = 0
        0x01,               // numPackStreams = 1
        SevenZipNid.Size,
        0x01,               // size[0] = 1

        SevenZipNid.Crc,
        0x02,               // AllAreDefined = 2 (некорректно)

        SevenZipNid.End,
    ];

    SevenZipPackInfoReadResult res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }
}
