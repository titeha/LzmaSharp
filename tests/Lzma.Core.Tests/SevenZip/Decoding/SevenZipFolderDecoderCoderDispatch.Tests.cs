using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderCoderDispatchTests
{
  [Fact]
  public void DecodeFolderToArray_Copy_НесовпадениеРазмера_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        coder: new SevenZipCoderInfo(
            methodId: [0x00], // Copy
            properties: [],
            numInStreams: 1,
            numOutStreams: 1),
        packSize: 3UL,
        unpackSize: 4UL);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_НеизвестныйMethodId_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCoderStreamsInfo(
        coder: new SevenZipCoderInfo(
            methodId: [0x7F, 0x7F],
            properties: [],
            numInStreams: 1,
            numOutStreams: 1),
        packSize: 3UL,
        unpackSize: 3UL);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateSingleCoderStreamsInfo(
      SevenZipCoderInfo coder,
      ulong packSize,
      ulong unpackSize)
  {
    var folder = new SevenZipFolder(
        Coders: [coder],
        BindPairs: [],
        PackedStreamIndices: [0UL],
        NumInStreams: 1,
        NumOutStreams: 1);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [packSize]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [unpackSize],
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
