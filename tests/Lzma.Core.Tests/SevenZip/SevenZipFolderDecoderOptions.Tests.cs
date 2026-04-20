using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderOptionsTests
{
  [Fact]
  public void DecodeFolderToArray_СтараяПерегрузка_ИспользуетПоведениеПоУмолчанию()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packSize: 3UL,
        unpackSize: 3UL);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(packedStreams, output);
  }

  [Fact]
  public void DecodeFolderToArray_НоваяПерегрузкаСDefaultOptions_СохраняетПоведение()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packSize: 3UL,
        unpackSize: 3UL);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(packedStreams, output);
  }

  [Fact]
  public void DecodeFolderToArray_НоваяПерегрузкаСПаролем_НеМеняетПоведениеОбычногоCopy()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packSize: 3UL,
        unpackSize: 3UL);

    byte[] packedStreams = [0x10, 0x20, 0x30];

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        options: options,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(packedStreams, output);
  }

  [Fact]
  public void DecodeFolderToArray_НоваяПерегрузкаСNullOptions_БросаетArgumentNullException()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleCopyStreamsInfo(
        packSize: 3UL,
        unpackSize: 3UL);

    Assert.Throws<ArgumentNullException>(
        () => SevenZipFolderDecoder.DecodeFolderToArray(
            streamsInfo: streamsInfo,
            packedStreams: [0x10, 0x20, 0x30],
            folderIndex: 0,
            options: null!,
            output: out _));
  }

  [Fact]
  public void DecodeFolderToArray_AesСПаролем_ПокаВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleAesCoderStreamsInfo(
        aesProperties: []);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        options: options,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateSingleCopyStreamsInfo(
      ulong packSize,
      ulong unpackSize)
  {
    var coder = new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
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
