using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderMultiFolderPackStreamTests
{
  [Fact]
  public void DecodeFolderToArray_ВторойFolder_БеретВторойPackStream()
  {
    byte[] packedStreams =
    [
      0xA0, 0xA1,             // pack stream #0 для folder0
      0xB0, 0xB1, 0xB2,       // pack stream #1 для folder1
    ];

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [2UL, 3UL]);

    var folder0 = CreateSingleCopyFolder();
    var folder1 = CreateSingleCopyFolder();

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder0, folder1],
        folderUnpackSizes:
        [
          [2UL],
          [3UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0xB0, 0xB1, 0xB2 }, output);
  }

  [Fact]
  public void DecodeFolderToArray_ВторойFolder_УчитываетPackPos()
  {
    byte[] packedStreams =
    [
      0xEE, 0xFF,             // prefix до PackPos
      0xA0, 0xA1,             // pack stream #0 для folder0
      0xB0, 0xB1, 0xB2,       // pack stream #1 для folder1
    ];

    var packInfo = new SevenZipPackInfo(
        packPos: 2,
        packSizes: [2UL, 3UL]);

    var folder0 = CreateSingleCopyFolder();
    var folder1 = CreateSingleCopyFolder();

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder0, folder1],
        folderUnpackSizes:
        [
          [2UL],
          [3UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0xB0, 0xB1, 0xB2 }, output);
  }

  private static SevenZipFolder CreateSingleCopyFolder()
  {
    return new SevenZipFolder(
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
  }
}
