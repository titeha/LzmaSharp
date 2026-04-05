using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSubStreamsInfoReaderSectionValidationTests
{
  [Fact]
  public void TryRead_EndWithoutSize_AndNumUnpackStreamGreaterThanOne_ReturnsNotSupported()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x02,
      SevenZipNid.End,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NotSupported, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_DuplicateNumUnpackStream_ReturnsInvalidData()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x01,
      SevenZipNid.NumUnpackStream,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_NumUnpackStreamZero_ReturnsInvalidData()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x00,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_DuplicateSize_ReturnsInvalidData()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Size,
      SevenZipNid.Size,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_DuplicateCrc_ReturnsInvalidData()
  {
    var unpackInfo = CreateUnpackInfo(
      folderUnpackSize: 10,
      folderCrcDefined: [true],
      folderCrc: [0x11223344u]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Crc,
      0x01,
      SevenZipNid.Crc,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  private static SevenZipUnpackInfo CreateUnpackInfo(
    ulong folderUnpackSize,
    bool[]? folderCrcDefined = null,
    uint[]? folderCrc = null)
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    return new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[folderUnpackSize]],
      folderCrcDefined: folderCrcDefined,
      folderCrc: folderCrc);
  }
}
