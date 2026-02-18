using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipUnpackInfoReaderFolderCrcDefinedTests
{
  [Fact]
  public void TryRead_Crc_AllAreDefined_ЗаполняетFolderCrcDefined()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x02, // NumFolders = 2
      0x00, // External = 0

      // Folder[0]
      0x01, 0x01, 0x21,
      // Folder[1]
      0x01, 0x01, 0x21,

      SevenZipNid.CodersUnpackSize,
      0x05,
      0x06,

      SevenZipNid.Crc,
      0x01, // AllAreDefined = 1
      0x11, 0x22, 0x33, 0x44,
      0x55, 0x66, 0x77, 0x88,

      SevenZipNid.End,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpack, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.NotNull(unpack.FolderCrcDefined);
    Assert.Equal(new[] { true, true }, unpack.FolderCrcDefined!);
  }

  [Fact]
  public void TryRead_Crc_PartialDefined_ЗаполняетFolderCrcDefined()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x02,
      0x00,

      0x01, 0x01, 0x21,
      0x01, 0x01, 0x21,

      SevenZipNid.CodersUnpackSize,
      0x05,
      0x06,

      SevenZipNid.Crc,
      0x00, // AllAreDefined = 0
      0x40, // Defined: [false, true]
      0x11, 0x22, 0x33, 0x44,

      SevenZipNid.End,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpack, out int consumed);

    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.NotNull(unpack.FolderCrcDefined);
    Assert.Equal(new[] { false, true }, unpack.FolderCrcDefined!);
  }
}
