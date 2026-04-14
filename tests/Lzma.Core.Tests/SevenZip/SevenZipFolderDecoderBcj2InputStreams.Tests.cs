using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2InputStreamsTests
{
  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_CopyProducerИRawВход_ВозвращаетПотокиВПорядкеСлотовBcj2()
  {
    // Порядок packed stream'ов намеренно не совпадает
    // ни с порядком coders, ни с порядком BCJ2 slot'ов.
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xB0,                         // ord2 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> CopyA.in (global in=5)
    ];

    SevenZipStreamsInfo streamsInfo = CreateBcj2CopyProducerScenario(
        packedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        packSizes: [3UL, 2UL, 1UL, 4UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(4, decoded.Length);

    // BCJ2 slot0 <- CopyA
    Assert.Equal(new byte[] { 0xA0, 0xA1, 0xA2, 0xA3 }, decoded[0]);

    // BCJ2 slot1 <- raw unbound packed stream
    Assert.Equal(new byte[] { 0xD0, 0xD1 }, decoded[1]);

    // BCJ2 slot2 <- CopyC
    Assert.Equal(new byte[] { 0xC0, 0xC1, 0xC2 }, decoded[2]);

    // BCJ2 slot3 <- CopyB
    Assert.Equal(new byte[] { 0xB0 }, decoded[3]);
  }

  [Fact]
  public void TryDecodeBcj2InputStreamsToArrays_UnboundBcj2ВходОтсутствуетВPackedStreamIndices_ВозвращаетInvalidData()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xB0,                         // ord1 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord2 -> CopyA.in (global in=5)
      0xEE,                         // ord3 -> лишний stream
    ];

    // Здесь намеренно нет global in=2 для raw BCJ2 slot1.
    SevenZipStreamsInfo streamsInfo = CreateBcj2CopyProducerScenario(
        packedStreamIndices: [6UL, 0UL, 5UL, 1UL],
        packSizes: [3UL, 1UL, 4UL, 1UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  private static SevenZipStreamsInfo CreateBcj2CopyProducerScenario(
      ulong[] packedStreamIndices,
      ulong[] packSizes)
  {
    // Порядок coders намеренно нетривиален.
    //
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
          // BCJ2 slot0 (consumerIn=1) <- CopyA.out2
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),

          // BCJ2 slot2 (consumerIn=3) <- CopyC.out3
          new SevenZipBindPair(InIndex: 3, OutIndex: 3),

          // BCJ2 slot3 (consumerIn=4) <- CopyB.out0
          new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        PackedStreamIndices: packedStreamIndices,
        NumInStreams: 7,
        NumOutStreams: 4);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: packSizes);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [
            1UL,    // out0 = CopyB
            123UL,  // out1 = финальный BCJ2 output; здесь helper его не использует
            4UL,    // out2 = CopyA
            3UL,    // out3 = CopyC
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

  private static SevenZipCoderInfo CreateBcj2Coder()
  {
    return new SevenZipCoderInfo(
        methodId: [0x1B],
        properties: [],
        numInStreams: 4,
        numOutStreams: 1);
  }
}
