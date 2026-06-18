using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2ProducerCodersTests
{
  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_ProducerCoderНеПоддерживается_ВозвращаетNotSupported()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xB0,                         // ord2 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> producer slot0 .in (global in=5)
    ];

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        slot0ProducerCoder: CreateDeltaCoder(),
        slot0ExpectedUnpackSize: 4UL);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_CopyProducerРазмерНеСовпадает_ВозвращаетInvalidData()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xB0,                         // ord2 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> producer slot0 .in (global in=5)
    ];

    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        slot0ProducerCoder: CreateCopyCoder(),
        slot0ExpectedUnpackSize: 5UL);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  private static SevenZipStreamsInfo CreateStreamsInfo(
      SevenZipCoderInfo slot0ProducerCoder,
      ulong slot0ExpectedUnpackSize)
  {
    // Глобальные inIndex:
    //   CopyB = 0
    //   BCJ2  = 1..4
    //   slot0 producer = 5
    //   CopyC = 6
    //
    // Глобальные outIndex:
    //   CopyB = 0
    //   BCJ2  = 1
    //   slot0 producer = 2
    //   CopyC = 3
    var folder = new SevenZipFolder(
        Coders:
        [
          CreateCopyCoder(),      // coder 0 = CopyB
          CreateBcj2Coder(),      // coder 1 = BCJ2
          slot0ProducerCoder,     // coder 2 = producer для slot0
          CreateCopyCoder(),      // coder 3 = CopyC
        ],
        BindPairs:
        [
          // BCJ2 slot0 (consumerIn=1) <- producer.out2
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),

          // BCJ2 slot2 (consumerIn=3) <- CopyC.out3
          new SevenZipBindPair(InIndex: 3, OutIndex: 3),

          // BCJ2 slot3 (consumerIn=4) <- CopyB.out0
          new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        PackedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        NumInStreams: 7,
        NumOutStreams: 4);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3UL, 2UL, 1UL, 4UL]);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [
            1UL,                    // out0 = CopyB
            123UL,                  // out1 = финальный BCJ2 output; helper его здесь не использует
            slot0ExpectedUnpackSize,// out2 = producer для slot0
            3UL,                    // out3 = CopyC
          ]
        ]);

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

  private static SevenZipCoderInfo CreateDeltaCoder()
  {
    return new SevenZipCoderInfo(
        methodId: [0x03],
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
