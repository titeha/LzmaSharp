using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderUnsupportedTopologyTests
{
  [Fact]
  public void DecodeFolderToArray_TwoIndependentCopyCodersInOneFolder_NotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
                new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0, 1],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
                [3, 4],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3, 4]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3, 4, 5, 6, 7];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_OnePackedStream_TwoCoders_WithoutBindPairs_NotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
                new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
                [3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }
}
