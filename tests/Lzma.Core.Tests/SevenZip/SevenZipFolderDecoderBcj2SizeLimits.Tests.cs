using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2SizeLimitsTests
{
  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_NumInStreamsПапкиБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        numInStreams: ((ulong)int.MaxValue) + 1,
        numOutStreams: 4UL,
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_NumOutStreamsПапкиБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        numInStreams: 7UL,
        numOutStreams: ((ulong)int.MaxValue) + 1,
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_РазмерРаспаковкиProducerБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        numInStreams: 7UL,
        numOutStreams: 4UL,
        folderUnpackSizes:
        [
          1UL,                       // out0 = CopyB
          123UL,                     // out1 = финальный BCJ2 output
          ((ulong)int.MaxValue) + 1, // out2 = CopyA -> BCJ2 slot0
          3UL,                       // out3 = CopyC
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_КоличествоUnpackSizesНеСовпадаетСNumOutStreams_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        numInStreams: 7UL,
        numOutStreams: 4UL,
        folderUnpackSizes:
        [
          1UL,
        123UL,
        4UL,
        ]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_NumInStreamsУCoderБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    // Важно: лимиты самой folder не превышаем,
    // чтобы попасть именно в проверку на уровне отдельного coder-а.
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
            methodId: [0x00],
            properties: [],
            numInStreams: ((ulong)int.MaxValue) + 1,
            numOutStreams: 1),
        CreateBcj2Coder(),
        CreateCopyCoder(),
        CreateCopyCoder(),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),
        new SevenZipBindPair(InIndex: 3, OutIndex: 3),
        new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        PackedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        NumInStreams: 7UL,
        NumOutStreams: 4UL);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfoForCustomFolder(
        folder: folder,
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_NumOutStreamsУCoderБольшеIntMaxValue_ВозвращаетNotSupported()
  {
    // Важно: лимиты самой folder не превышаем,
    // чтобы попасть именно в проверку на уровне отдельного coder-а.
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
            methodId: [0x00],
            properties: [],
            numInStreams: 1,
            numOutStreams: ((ulong)int.MaxValue) + 1),
        CreateBcj2Coder(),
        CreateCopyCoder(),
        CreateCopyCoder(),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),
        new SevenZipBindPair(InIndex: 3, OutIndex: 3),
        new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        PackedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        NumInStreams: 7UL,
        NumOutStreams: 4UL);

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfoForCustomFolder(
        folder: folder,
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: CreatePackedStreams(),
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  private static SevenZipStreamsInfo CreateStreamsInfoForCustomFolder(
      SevenZipFolder folder,
      ulong[] folderUnpackSizes)
  {
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL, 2UL, 1UL, 4UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [folderUnpackSizes]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }

  private static byte[] CreatePackedStreams()
  {
    return
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xB0,                         // ord2 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> CopyA.in (global in=5)
    ];
  }

  private static SevenZipStreamsInfo CreateStreamsInfo(
      ulong numInStreams,
      ulong numOutStreams,
      ulong[] folderUnpackSizes)
  {
    // Глобальные inIndex:
    //   CopyB = 0
    //   BCJ2  = 1..4
    //   CopyA = 5
    //   CopyC = 6
    //
    // Глобальные outIndex:
    //   CopyB = 0
    //   BCJ2  = 1
    //   CopyA = 2
    //   CopyC = 3
    var folder = new SevenZipFolder(
        Coders:
        [
          CreateCopyCoder(), // coder 0 = CopyB
          CreateBcj2Coder(), // coder 1 = BCJ2
          CreateCopyCoder(), // coder 2 = CopyA
          CreateCopyCoder(), // coder 3 = CopyC
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),
          new SevenZipBindPair(InIndex: 3, OutIndex: 3),
          new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        PackedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        NumInStreams: numInStreams,
        NumOutStreams: numOutStreams);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL, 2UL, 1UL, 4UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [folderUnpackSizes]);

    return new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);
  }

  private static SevenZipCoderInfo CreateCopyCoder()
  {
    return new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);
  }

  private static SevenZipCoderInfo CreateBcj2Coder()
  {
    return new SevenZipCoderInfo(
        methodId: [0x1B],
        properties: [],
        numInStreams: 4,
        numOutStreams: 1);
  }
}
