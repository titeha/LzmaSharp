using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderTopLevelValidationTests
{
  [Fact]
  public void DecodeFolderToArray_БезPackInfo_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = new(
        packInfo: null,
        unpackInfo: CreateUnpackInfoWithSingleCopyFolder(),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_БезUnpackInfo_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = new(
        packInfo: new SevenZipPackInfo(
            packPos: 0,
            packSizes: [1UL]),
        unpackInfo: null,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Theory]
  [InlineData(-1)]
  [InlineData(1)]
  public void DecodeFolderToArray_FolderIndexВыходитЗаДиапазон_ВозвращаетInvalidData(
      int folderIndex)
  {
    SevenZipStreamsInfo streamsInfo = new(
        packInfo: new SevenZipPackInfo(
            packPos: 0,
            packSizes: [1UL]),
        unpackInfo: CreateUnpackInfoWithSingleCopyFolder(),
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: folderIndex,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static SevenZipUnpackInfo CreateUnpackInfoWithSingleCopyFolder()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
              methodId: [0x00],
              properties: [],
              numInStreams: 1,
              numOutStreams: 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0UL],
        NumInStreams: 1,
        NumOutStreams: 1);

    return new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [1UL],
        ]);
  }
}
