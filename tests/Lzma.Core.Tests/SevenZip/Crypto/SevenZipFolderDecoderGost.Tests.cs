using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderGostTests
{
  public static TheoryData<byte[]> KnownGostMethodIds => new()
  {
    SevenZipGostCoder.KuznyechikMethodId.ToArray(),
    SevenZipGostCoder.MagmaMethodId.ToArray(),
  };

  [Theory]
  [MemberData(nameof(KnownGostMethodIds))]
  public void DecodeFolderToArray_GostСВалиднымиProperties_ВозвращаетNotSupported(byte[] methodId)
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleGostCoderStreamsInfo(
        methodId: methodId,
        gostProperties:
        [
          0x01, // version
          0x00, // flags
          0x03, // numCyclesPower
          0x00, // saltSize
          0x00, // ivSize
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Theory]
  [MemberData(nameof(KnownGostMethodIds))]
  public void DecodeFolderToArray_GostСНекорректнымиProperties_ВозвращаетInvalidData(byte[] methodId)
  {
    SevenZipStreamsInfo streamsInfo = CreateSingleGostCoderStreamsInfo(
        methodId: methodId,
        gostProperties:
        [
          0x01,
          0x00,
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Theory]
  [MemberData(nameof(KnownGostMethodIds))]
  public void DecodeFolderToArray_GostСПаролемИПустымIv_ВозвращаетInvalidData(byte[] methodId)
  {
    // numCyclesPower=0x03 теперь обрабатывается парольным KDF через Стрибог
    // (путь с паролем больше не NotSupported). При пустом IV (ivSize=0) шифр
    // отвергает вектор инициализации — для обоих шифров это InvalidData
    // (Кузнечик ждёт 8 байт, Магма — 4). Криптокорректность round-trip
    // проверяется отдельно в SevenZipFolderDecoderGostDecryptTests.
    SevenZipStreamsInfo streamsInfo = CreateSingleGostCoderStreamsInfo(
        methodId: methodId,
        gostProperties:
        [
          0x01, // version
          0x00, // flags
          0x03, // numCyclesPower
          0x00, // saltSize
          0x00, // ivSize
        ]);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: [0x10, 0x20, 0x30],
        folderIndex: 0,
        options: options,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateSingleGostCoderStreamsInfo(
      byte[] methodId,
      byte[] gostProperties)
  {
    var coder = new SevenZipCoderInfo(
        methodId: methodId,
        properties: gostProperties,
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
