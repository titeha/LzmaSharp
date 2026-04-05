using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSubStreamsInfoReaderCrcAndTopologyValidationTests
{
  [Fact]
  public void TryRead_Crc_AllAreDefinedHasInvalidValue_ReturnsInvalidData()
  {
    var unpackInfo = CreateSingleOutputUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Crc,
      0x02,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Crc_DefinedBitsTruncated_ReturnsNeedMoreInput()
  {
    var unpackInfo = CreateSingleOutputUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x02,
      SevenZipNid.Crc,
      0x00,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Crc_BytesTruncated_ReturnsNeedMoreInput()
  {
    var unpackInfo = CreateSingleOutputUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Crc,
      0x01,
      0x44,
      0x33,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Crc_FolderCrcDefinedLengthMismatch_ReturnsInvalidData()
  {
    var folder0 = CreateSingleOutputFolder();
    var folder1 = CreateSingleOutputFolder();

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder0, folder1],
      folderUnpackSizes: [[10UL], [20UL]],
      folderCrcDefined: [true],
      folderCrc: [0x11111111u, 0x22222222u]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Crc,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Crc_FolderCrcDefinedTrueWithoutFolderCrc_ReturnsInvalidData()
  {
    var unpackInfo = new SevenZipUnpackInfo(
      folders: [CreateSingleOutputFolder()],
      folderUnpackSizes: [[10UL]],
      folderCrcDefined: [true]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Crc,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Size_FolderUnpackSizesLengthMismatch_ReturnsInvalidData()
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 2);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[10UL]]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Size,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Size_MultipleFinalOutputs_ReturnsNotSupported()
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 2);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[10UL, 20UL]]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Size,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NotSupported, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Size_BindPairOutOfRange_ReturnsInvalidData()
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [new SevenZipBindPair(InIndex: 0, OutIndex: 1)],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[10UL]]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.Size,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out int bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  private static SevenZipUnpackInfo CreateSingleOutputUnpackInfo(ulong folderUnpackSize)
  {
    return new SevenZipUnpackInfo(
      folders: [CreateSingleOutputFolder()],
      folderUnpackSizes: [[folderUnpackSize]]);
  }

  private static SevenZipFolder CreateSingleOutputFolder()
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    return new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);
  }
}
