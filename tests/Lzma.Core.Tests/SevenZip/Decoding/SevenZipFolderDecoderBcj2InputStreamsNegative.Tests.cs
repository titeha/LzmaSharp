using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2InputStreamsNegativeTests
{
  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_НеЧетыреPackedStream_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders: [CreateBcj2Coder()],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL],
        NumInStreams: 4,
        NumOutStreams: 1);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folder: folder,
        packSizes: [1UL, 1UL, 1UL],
        folderUnpackSizes: [[1UL]]);

    byte[] packedStreams = [0x10, 0x11, 0x12];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_БезBcj2Coder_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          CreateCopyCoder(),
          CreateCopyCoder(),
          CreateCopyCoder(),
          CreateCopyCoder(),
        ],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL, 3UL],
        NumInStreams: 4,
        NumOutStreams: 4);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folder: folder,
        packSizes: [1UL, 1UL, 1UL, 1UL],
        folderUnpackSizes: [[1UL, 1UL, 1UL, 1UL]]);

    byte[] packedStreams = [0x10, 0x11, 0x12, 0x13];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_ДваBcj2Coder_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          CreateBcj2Coder(),
          CreateBcj2Coder(),
        ],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL, 3UL],
        NumInStreams: 8,
        NumOutStreams: 2);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folder: folder,
        packSizes: [1UL, 1UL, 1UL, 1UL],
        folderUnpackSizes: [[1UL, 1UL]]);

    byte[] packedStreams = [0x20, 0x21, 0x22, 0x23];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_Bcj2CoderНе4In1Out_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders: [CreateBcj2Coder(numInStreams: 4, numOutStreams: 2)],
        BindPairs: [],
        PackedStreamIndices: [0UL, 1UL, 2UL, 3UL],
        NumInStreams: 4,
        NumOutStreams: 2);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folder: folder,
        packSizes: [1UL, 1UL, 1UL, 1UL],
        folderUnpackSizes: [[1UL, 1UL]]);

    byte[] packedStreams = [0x30, 0x31, 0x32, 0x33];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_ProducerНе1In1Out_ВозвращаетNotSupported()
  {
    // coder0: producer с 2 входами и 1 выходом
    // coder1: BCJ2 с корректными 4 входами и 1 выходом
    //
    // Глобальные inIndex:
    //   coder0 = 0, 1
    //   bcj2   = 2, 3, 4, 5
    //
    // BindPair связывает out0 producer-а с первым входом BCJ2 (in2).
    // Остальные входы BCJ2 остаются unbound и доступны напрямую из packed stream.
    var folder = new SevenZipFolder(
        Coders:
        [
          CreateCopyCoder(numInStreams: 2, numOutStreams: 1),
          CreateBcj2Coder(),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 2, OutIndex: 0),
        ],
        PackedStreamIndices: [0UL, 3UL, 4UL, 5UL],
        NumInStreams: 6,
        NumOutStreams: 2);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        folder: folder,
        packSizes: [1UL, 1UL, 1UL, 1UL],
        folderUnpackSizes: [[1UL, 1UL]]);

    byte[] packedStreams = [0x40, 0x41, 0x42, 0x43];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  private static SevenZipStreamsInfo CreateStreamsInfo(
      SevenZipFolder folder,
      ulong[] packSizes,
      ulong[][] folderUnpackSizes)
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: packSizes);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: folderUnpackSizes);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }

  private static SevenZipCoderInfo CreateCopyCoder(
      ulong numInStreams = 1,
      ulong numOutStreams = 1)
  {
    return new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: numInStreams,
        numOutStreams: numOutStreams);
  }

  private static SevenZipCoderInfo CreateBcj2Coder(
      ulong numInStreams = 4,
      ulong numOutStreams = 1)
  {
    return new SevenZipCoderInfo(
        methodId: [0x1B],
        properties: [],
        numInStreams: numInStreams,
        numOutStreams: numOutStreams);
  }
}
