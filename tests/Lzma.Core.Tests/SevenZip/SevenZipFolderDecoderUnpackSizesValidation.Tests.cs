using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderUnpackSizesValidationTests
{
  [Fact]
  public void DecodeFolderToArray_FolderUnpackSizesКорочеFolders_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folderUnpackSizes: []);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_FolderUnpackSizesДляFolderNull_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folderUnpackSizes: [null!]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_FolderUnpackSizesДляFolderПустой_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folderUnpackSizes:
        [
          [],
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateStreamsInfo(ulong[][] folderUnpackSizes)
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

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [1UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: folderUnpackSizes);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
