using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2InputStreamsTopLevelValidationTests
{
  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_БезPackInfo_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: null,
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_БезUnpackInfo_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: null,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(1)]
  public void TryDecodeBcj2InputStreamsToArrays_FolderIndexВыходитЗаFolders_ВозвращаетInvalidData(
      int folderIndex)
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: folderIndex,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_FolderIndexВыходитЗаFolderUnpackSizes_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: CreateUnpackInfo(
            folders:
            [
              CreateBcj2Folder(),
              CreateBcj2Folder(),
            ],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 1,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_FolderUnpackSizesДляFolderNull_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes: [null!]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_FolderUnpackSizesДляFolderПустой_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes:
            [
              [],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_ПапкаБезCoder_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders: [],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL, 3UL],
        NumInStreams: 4,
        NumOutStreams: 1);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: CreatePackInfo(),
        unpackInfo: CreateUnpackInfo(
            folders: [folder],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_PackSizesМеньшеЧемЧетыреPackedStream_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: new SevenZipPackInfo(
            packPos: 0,
            packSizes: [1UL, 1UL, 1UL]),
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_PackedStreamВыходитЗаГраницыБуфера_ВозвращаетInvalidData()
  {
    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: new SevenZipPackInfo(
            packPos: 0,
            packSizes: [1UL, 1UL, 1UL, 2UL]),
        unpackInfo: CreateUnpackInfo(
            folders: [CreateBcj2Folder()],
            folderUnpackSizes:
            [
              [1UL],
            ]),
        subStreamsInfo: null);

    // Суммарный размер по PackSizes = 5,
    // а реально packedStreams содержит только 4 байта.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x11, 0x12, 0x13],
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  private static SevenZipPackInfo CreatePackInfo()
  {
    return new SevenZipPackInfo(
        packPos: 0,
        packSizes: [1UL, 1UL, 1UL, 1UL]);
  }

  private static SevenZipUnpackInfo CreateUnpackInfo(
      SevenZipFolder[] folders,
      ulong[][] folderUnpackSizes)
  {
    return new SevenZipUnpackInfo(
        folders: folders,
        folderUnpackSizes: folderUnpackSizes);
  }

  private static SevenZipFolder CreateBcj2Folder()
  {
    return new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
              methodId: [0x1B],
              properties: [],
              numInStreams: 4,
              numOutStreams: 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL, 3UL],
        NumInStreams: 4,
        NumOutStreams: 1);
  }
}
