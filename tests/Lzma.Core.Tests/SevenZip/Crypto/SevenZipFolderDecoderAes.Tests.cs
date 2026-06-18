using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderAesTests
{
  [Fact]
  public void DecodeFolderToArray_Aes_СВалиднымиProperties_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleAesCoderStreamsInfo(
        aesProperties: []);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Aes_СНекорректнымиProperties_ВозвращаетInvalidData()
  {
    // 0xD3 сообщает, что salt / IV есть,
    // но второго байта свойств уже нет.
    SevenZipStreamsInfo streamsInfo = CreateSingleAesCoderStreamsInfo(
        aesProperties: [0xD3]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Aes_СНеподдерживаемымNumCyclesPower_ВозвращаетNotSupported()
  {
    // 25 — корректно разобранное значение,
    // но выше текущего поддерживаемого лимита 24.
    SevenZipStreamsInfo streamsInfo = CreateSingleAesCoderStreamsInfo(
        aesProperties: [25]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateSingleAesCoderStreamsInfo(
      byte[] aesProperties)
  {
    var coder = new SevenZipCoderInfo(
        methodId: [0x06, 0xF1, 0x07, 0x01],
        properties: aesProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders: [coder],
        BindPairs: [],
        PackedStreamIndices: [0UL],
        NumInStreams: 1,
        NumOutStreams: 1);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3UL],
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
