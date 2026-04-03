using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderSizeLimitsTests
{
    [Fact]
    public void DecodeFolderToArray_FolderUnpackSizesCountDoesNotMatchCoderCount_ReturnsNotSupported()
    {
        SevenZipCoderInfo[] coders =
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
            new SevenZipCoderInfo([0x00], [], 1, 1),
        ];

        var folder = new SevenZipFolder(
            Coders: coders,
            BindPairs: [new SevenZipBindPair(InIndex: 1, OutIndex: 0)],
            PackedStreamIndices: [0],
            NumInStreams: 2,
            NumOutStreams: 2);

        var unpackInfo = new SevenZipUnpackInfo(
            folders: [folder],
            folderUnpackSizes:
            [
                [3],
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

    [Fact]
    public void DecodeFolderToArray_ExpectedUnpackSizeGreaterThanIntMaxValue_ReturnsNotSupported()
    {
        var coder = new SevenZipCoderInfo(
            methodId: [0x00],
            properties: [],
            numInStreams: 1,
            numOutStreams: 1);

        var folder = new SevenZipFolder(
            Coders: [coder],
            BindPairs: [],
            PackedStreamIndices: [0],
            NumInStreams: 1,
            NumOutStreams: 1);

        var unpackInfo = new SevenZipUnpackInfo(
            folders: [folder],
            folderUnpackSizes:
            [
                [((ulong)int.MaxValue) + 1],
            ]);

        var packInfo = new SevenZipPackInfo(
            packPos: 0,
            packSizes: [0]);

        var streamsInfo = new SevenZipStreamsInfo(
            packInfo: packInfo,
            unpackInfo: unpackInfo,
            subStreamsInfo: null);

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams: [],
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
        Assert.Empty(output);
    }
}
