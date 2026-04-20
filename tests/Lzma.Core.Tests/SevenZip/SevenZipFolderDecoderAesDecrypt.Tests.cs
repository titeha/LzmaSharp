using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderAesDecryptTests
{
  [Fact]
  public void DecodeFolderToArray_AesCopyPipeline_СDirectKey_ДекодируетЗашифрованныйCopyВход()
  {
    // AES-256-CBC:
    // key = 32 нулевых байта,
    // iv = 16 нулевых байт,
    // plaintext = 16 нулевых байт.
    byte[] encryptedZeros = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    SevenZipStreamsInfo streamsInfo = CreateAesThenCopyStreamsInfo(
        aesUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 16UL,
        aesProperties: [SevenZipAesCoder.DirectKeyNumCyclesPower]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encryptedZeros,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[16], output);
  }

  [Fact]
  public void DecodeFolderToArray_AesCopyPipeline_БезПароля_ВозвращаетNotSupported()
  {
    byte[] encryptedZeros = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    SevenZipStreamsInfo streamsInfo = CreateAesThenCopyStreamsInfo(
        aesUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 16UL,
        aesProperties: [SevenZipAesCoder.DirectKeyNumCyclesPower]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encryptedZeros,
        folderIndex: 0,
        options: SevenZipDecodeOptions.Default,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_AesCopyPipeline_НекратныйБлокуCiphertext_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateAesThenCopyStreamsInfo(
        aesUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        packSize: 15UL,
        aesProperties: [SevenZipAesCoder.DirectKeyNumCyclesPower]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: new byte[15],
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_AesCopyPipeline_ПослеРасшифровкиРазмерНеСовпадает_ВозвращаетInvalidData()
  {
    byte[] encryptedZeros = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    SevenZipStreamsInfo streamsInfo = CreateAesThenCopyStreamsInfo(
        aesUnpackSize: 15UL,
        finalUnpackSize: 16UL,
        packSize: 16UL,
        aesProperties: [SevenZipAesCoder.DirectKeyNumCyclesPower]);

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: encryptedZeros,
        folderIndex: 0,
        options: SevenZipDecodeOptions.WithPassword(password),
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateAesThenCopyStreamsInfo(
      ulong aesUnpackSize,
      ulong finalUnpackSize,
      ulong packSize,
      byte[] aesProperties)
  {
    var aesCoder = new SevenZipCoderInfo(
        methodId: [0x06, 0xF1, 0x07, 0x01],
        properties: aesProperties,
        numInStreams: 1,
        numOutStreams: 1);

    var copyCoder = new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders:
        [
          aesCoder,
          copyCoder,
        ],
        BindPairs:
        [
          // AES.out0 -> Copy.in1
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        ],
        PackedStreamIndices: [0UL],
        NumInStreams: 2,
        NumOutStreams: 2);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [packSize]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [
            aesUnpackSize,
            finalUnpackSize,
          ],
        ]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }
}
