using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderPackedStreamRangesTests
{
  [Fact]
  public void TryGetFolderPackedStreamRanges_ВторойFolder_ВозвращаетКорректныеДиапазоныИСопоставлениеInIndex()
  {
    byte[] packedStreams =
    [
      0xEE, 0xFF,             // префикс до PackPos
      0xA0, 0xA1, 0xA2,       // global pack stream #0 (folder0)
      0xB0, 0xB1, 0xB2, 0xB3, // global pack stream #1 (folder1, inIndex=1)
      0xC0, 0xC1,             // global pack stream #2 (folder1, inIndex=0)
    ];

    var packInfo = new SevenZipPackInfo(
        packPos: 2,
        packSizes: [3UL, 4UL, 2UL]);

    var folder0 = new SevenZipFolder(
        Coders: [CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    // Здесь специально делаем нетривиальный порядок PackedStreamIndices,
    // чтобы проверить, что helper возвращает именно FolderInIndex из модели folder'а,
    // а не просто 0..N-1.
    var folder1 = new SevenZipFolder(
        Coders: [CreateCopyCoder(), CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [1, 0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder0, folder1],
        folderUnpackSizes:
        [
          [3UL],
          [4UL, 2UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 1,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(2, ranges.Length);

    Assert.Equal(1UL, ranges[0].FolderInIndex);
    Assert.Equal(1u, ranges[0].PackStreamIndex);
    Assert.Equal(5, ranges[0].Offset);
    Assert.Equal(4, ranges[0].Length);

    Assert.Equal(0UL, ranges[1].FolderInIndex);
    Assert.Equal(2u, ranges[1].PackStreamIndex);
    Assert.Equal(9, ranges[1].Offset);
    Assert.Equal(2, ranges[1].Length);
  }

  [Fact]
  public void TryGetFolderPackedStreamRanges_РазмерPackStreamВыходитЗаГраницыБуфера_ВозвращаетInvalidData()
  {
    byte[] packedStreams = [0x10, 0x11, 0x20, 0x21, 0x22];

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [2UL, 4UL]);

    var folder = new SevenZipFolder(
        Coders: [CreateCopyCoder(), CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [0, 1],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [2UL, 4UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(ranges);
  }

  [Fact]
  public void TryGetFolderPackedStreamRanges_БезPackInfo_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders: [CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [1UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: null,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(ranges);
  }

  [Fact]
  public void TryGetFolderPackedStreamRanges_БезUnpackInfo_ВозвращаетInvalidData()
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [1UL]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: null,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(ranges);
  }

  [Fact]
  public void TryGetFolderPackedStreamRanges_FolderIndexВыходитЗаДиапазон_ВозвращаетInvalidData()
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [1UL]);

    var folder = new SevenZipFolder(
        Coders: [CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [1UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 1,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(ranges);
  }

  [Fact]
  public void TryGetFolderPackedStreamRanges_ПустыеPackedStreamIndices_ВозвращаетInvalidData()
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [1UL]);

    var folder = new SevenZipFolder(
        Coders: [CreateCopyCoder()],
        BindPairs: [],
        PackedStreamIndices: [],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [1UL],
        ]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo: streamsInfo,
        packedStreams: [0x10],
        folderIndex: 0,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(ranges);
  }

  private static SevenZipCoderInfo CreateCopyCoder()
  {
    return new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);
  }
}
