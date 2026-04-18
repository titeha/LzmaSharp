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

  [Theory]
  [InlineData(1)]
  [InlineData(3)]
  public void DecodeFolderToArray_КоличествоFolderUnpackSizesНеСовпадаетСЧисломCoder_ВозвращаетNotSupported(
    int unpackSizesCount)
  {
    SevenZipStreamsInfo streamsInfo = CreateTwoCoderStreamsInfo(
        folderUnpackSizes: CreateUnpackSizes(unpackSizesCount));

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static ulong[] CreateUnpackSizes(int count)
  {
    ulong[] sizes = new ulong[count];

    for (int i = 0; i < sizes.Length; i++)
      sizes[i] = 3UL;

    return sizes;
  }

  [Fact]
  public void DecodeFolderToArray_UnpackSizeБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folderUnpackSizes:
        [
          [((ulong)int.MaxValue) + 1UL],
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateTwoCoderStreamsInfo(ulong[] folderUnpackSizes)
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
            methodId: [0x00],
            properties: [],
            numInStreams: 1,
            numOutStreams: 1),
        new SevenZipCoderInfo(
            methodId: [0x00],
            properties: [],
            numInStreams: 1,
            numOutStreams: 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        ],
        PackedStreamIndices: [0UL],
        NumInStreams: 2,
        NumOutStreams: 2);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          folderUnpackSizes,
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
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
