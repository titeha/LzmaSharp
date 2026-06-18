using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipUnpackInfoReaderCrcTests
{
  [Fact]
  public void TryRead_CrcAllAreDefined_ОдинFolder_Работает()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01, // NumFolders = 1
      0x00, // External = 0

      // Folder[0]:
      0x01, // NumCoders = 1
      0x21, // mainByte: idSize=1, hasProps=1
      0x21, // methodId: LZMA2 (0x21)
      0x01, // propsSize = 1
      0x00, // props byte (dict=4KiB)

      SevenZipNid.CodersUnpackSize,
      0x05, // UnpackSize для единственного out-stream

      SevenZipNid.Crc,
      0x01, // AllAreDefined = 1
      0x44, 0x33, 0x22, 0x11, // CRC (1 шт)

      SevenZipNid.End,
    ];

    SevenZipUnpackInfoReadResult res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpack, out int bytesConsumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);

    Assert.Single(unpack.Folders);
    Assert.Single(unpack.FolderUnpackSizes);
    Assert.Equal(5UL, unpack.FolderUnpackSizes[0][0]);
  }

  [Fact]
  public void TryRead_CrcPartialDefined_ДваFolder_Работает()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x02, // NumFolders = 2
      0x00, // External = 0

      // Folder[0]
      0x01, 0x21, 0x21, 0x01, 0x00,
      // Folder[1]
      0x01, 0x21, 0x21, 0x01, 0x00,

      SevenZipNid.CodersUnpackSize,
      0x05, // unpack size folder0
      0x06, // unpack size folder1

      SevenZipNid.Crc,
      0x00, // AllAreDefined = 0
      0x40, // Defined = [false, true] (MSB->LSB)
      0x44, 0x33, 0x22, 0x11, // CRC только для defined (1 шт)

      SevenZipNid.End,
    ];

    SevenZipUnpackInfoReadResult res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpack, out int bytesConsumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);

    Assert.Equal(2, unpack.Folders.Length);
    Assert.Equal(2, unpack.FolderUnpackSizes.Length);
    Assert.Equal(5UL, unpack.FolderUnpackSizes[0][0]);
    Assert.Equal(6UL, unpack.FolderUnpackSizes[1][0]);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиCrcAllAreDefinedНе0ИНе1()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,

      0x01, 0x21, 0x21, 0x01, 0x00,

      SevenZipNid.CodersUnpackSize,
      0x05,

      SevenZipNid.Crc,
      0x02, // некорректно

      SevenZipNid.End,
    ];

    SevenZipUnpackInfoReadResult res = SevenZipUnpackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиОбрезаныCrcBytes()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,

      0x01, 0x21, 0x21, 0x01, 0x00,

      SevenZipNid.CodersUnpackSize,
      0x05,

      SevenZipNid.Crc,
      0x01, // AllAreDefined = 1
      0x44, 0x33, // CRC обрезан

      // End отсутствует
    ];

    SevenZipUnpackInfoReadResult res = SevenZipUnpackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }
}
