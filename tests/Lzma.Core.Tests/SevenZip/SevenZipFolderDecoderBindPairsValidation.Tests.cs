using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBindPairsValidationTests
{
    [Fact]
    public void DecodeFolderToArray_BindPairInIndexOutOfRange_ReturnsInvalidData()
    {
        SevenZipStreamsInfo streamsInfo = CreateTwoCoderStreamsInfo(
            new SevenZipBindPair(InIndex: 2, OutIndex: 0));

        byte[] packedStreams = [1, 2, 3];

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams,
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
        Assert.Empty(output);
    }

    [Fact]
    public void DecodeFolderToArray_BindPairOutIndexOutOfRange_ReturnsInvalidData()
    {
        SevenZipStreamsInfo streamsInfo = CreateTwoCoderStreamsInfo(
            new SevenZipBindPair(InIndex: 1, OutIndex: 2));

        byte[] packedStreams = [1, 2, 3];

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams,
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
        Assert.Empty(output);
    }

    [Fact]
    public void DecodeFolderToArray_BindPairSelfReference_ReturnsInvalidData()
    {
        SevenZipStreamsInfo streamsInfo = CreateTwoCoderStreamsInfo(
            new SevenZipBindPair(InIndex: 1, OutIndex: 1));

        byte[] packedStreams = [1, 2, 3];

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams,
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
        Assert.Empty(output);
    }

    [Fact]
    public void DecodeFolderToArray_TwoBindPairsShareSameConsumer_ReturnsInvalidData()
    {
        SevenZipStreamsInfo streamsInfo = CreateThreeCoderStreamsInfo(
            new SevenZipBindPair(InIndex: 2, OutIndex: 0),
            new SevenZipBindPair(InIndex: 2, OutIndex: 1));

        byte[] packedStreams = [1, 2, 3];

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams,
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
        Assert.Empty(output);
    }

    [Fact]
    public void DecodeFolderToArray_TwoBindPairsShareSameProducer_ReturnsInvalidData()
    {
        SevenZipStreamsInfo streamsInfo = CreateThreeCoderStreamsInfo(
            new SevenZipBindPair(InIndex: 1, OutIndex: 0),
            new SevenZipBindPair(InIndex: 2, OutIndex: 0));

        byte[] packedStreams = [1, 2, 3];

        SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo,
            packedStreams,
            folderIndex: 0,
            out byte[] output);

        Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
        Assert.Empty(output);
    }

    private static SevenZipStreamsInfo CreateTwoCoderStreamsInfo(SevenZipBindPair bindPair)
    {
        SevenZipCoderInfo[] coders =
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
            new SevenZipCoderInfo([0x00], [], 1, 1),
        ];

        var folder = new SevenZipFolder(
            Coders: coders,
            BindPairs: [bindPair],
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

        return new SevenZipStreamsInfo(
            packInfo: packInfo,
            unpackInfo: unpackInfo,
            subStreamsInfo: null);
    }

    private static SevenZipStreamsInfo CreateThreeCoderStreamsInfo(SevenZipBindPair first, SevenZipBindPair second)
    {
        SevenZipCoderInfo[] coders =
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
            new SevenZipCoderInfo([0x00], [], 1, 1),
            new SevenZipCoderInfo([0x00], [], 1, 1),
        ];

        var folder = new SevenZipFolder(
            Coders: coders,
            BindPairs: [first, second],
            PackedStreamIndices: [0],
            NumInStreams: 3,
            NumOutStreams: 3);

        var unpackInfo = new SevenZipUnpackInfo(
            folders: [folder],
            folderUnpackSizes:
            [
                [3, 3, 3],
            ]);

        var packInfo = new SevenZipPackInfo(
            packPos: 0,
            packSizes: [3]);

        return new SevenZipStreamsInfo(
            packInfo: packInfo,
            unpackInfo: unpackInfo,
            subStreamsInfo: null);
    }
}
