using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipHeaderReaderTests
{
  [Fact]
  public void TryRead_NeedMoreInput_ЕслиБуферПустой()
  {
    var res = SevenZipHeaderReader.TryRead(
      [],
      out var header,
      out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);

    // Просто убеждаемся, что метод не бросает и не пишет мусор.
    _ = header;
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиПервыйБайтНеHeader()
  {
    byte[] data = [0xFF];

    var res = SevenZipHeaderReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиEncodedHeader()
  {
    byte[] data = [SevenZipNid.EncodedHeader];

    var res = SevenZipHeaderReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_Ok_МинимальныйHeader_СПустымиСекциями()
  {
    // Header
    //   MainStreamsInfo (пусто)
    //   FilesInfo (0 файлов)
    // End
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.End,
      SevenZipNid.FilesInfo,
      0x00, // numFiles = 0
      SevenZipNid.End,
      SevenZipNid.End,
    ];

    var res = SevenZipHeaderReader.TryRead(
      data,
      out var header,
      out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);
    Assert.Equal(0UL, header.FilesInfo.FileCount);
  }

  [Fact]
  public void TryRead_Ok_Header_СArchiveProperties_ИAdditionalStreamsInfo()
  {
    // Header
    // ArchiveProperties: [Dummy(0x19), size=1, data=0xAA], End
    // AdditionalStreamsInfo: StreamsInfo пустой (End)
    // MainStreamsInfo (пусто)
    // FilesInfo (0 файлов)
    // End

    byte[] data =
    [
      SevenZipNid.Header,

    SevenZipNid.ArchiveProperties,
    0x19, // PropertyType (kDummy по спекам, но мы всё равно не интерпретируем)
    0x01, // PropertySize = 1 (EncodedUInt64)
    0xAA, // PropertyData[1]
    SevenZipNid.End,

    SevenZipNid.AdditionalStreamsInfo,
    SevenZipNid.End, // StreamsInfo.End

    SevenZipNid.MainStreamsInfo,
    SevenZipNid.End,

    SevenZipNid.FilesInfo,
    0x00,            // NumFiles = 0
    SevenZipNid.End, // end FilesInfo properties

    SevenZipNid.End, // end Header
  ];

    var res = SevenZipHeaderReader.TryRead(data, out var header, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);
    Assert.Equal(0UL, header.FilesInfo.FileCount);
  }
}
