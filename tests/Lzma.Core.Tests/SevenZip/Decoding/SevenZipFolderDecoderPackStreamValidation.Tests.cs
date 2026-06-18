using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderPackStreamValidationTests
{
  [Fact]
  public void DecodeFolderToArray_ИндексPackStreamВыходитЗаPackSizes_ВозвращаетInvalidData()
  {
    var folder0 = CreateSingleCopyFolder();
    var folder1 = CreateSingleCopyFolder();

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder0, folder1],
        folderUnpackSizes:
        [
          [3UL],
          [3UL],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL]); // Для folderIndex=1 нужен уже второй pack stream, а его нет.

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_PackPosВыходитЗаГраницыPackedStreams_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packPos: 4UL,
        packSize: 1UL,
        unpackSize: 1UL);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_РазмерPackStreamВыходитЗаГраницыPackedStreams_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packPos: 1UL,
        packSize: 3UL,
        unpackSize: 3UL);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
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

  private static SevenZipStreamsInfo CreateSingleCopyStreamsInfo(
      ulong packPos,
      ulong packSize,
      ulong unpackSize)
  {
    var folder = CreateSingleCopyFolder();

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [unpackSize],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: packPos,
        packSizes: [packSize]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
