using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2FinalOutputTests
{
  [Fact]
  public void DecodeFolderToArray_Bcj2_НесколькоФинальныхВыходов_ВозвращаетNotSupported()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xE0,                         // ord2 -> raw BCJ2 slot3 (global in=4)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> CopyA.in (global in=5)
    ];

    // Здесь CopyB намеренно остаётся "висячим":
    // его out0 никто не использует и он не участвует в сборке входов BCJ2.
    // В итоге финальными становятся сразу два выхода: out0 (CopyB) и out1 (BCJ2).
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        packedStreamIndices: [6UL, 2UL, 4UL, 5UL],
        bindPairs:
        [
          // BCJ2 slot0 (consumerIn=1) <- CopyA.out2
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),

          // BCJ2 slot2 (consumerIn=3) <- CopyC.out3
          new SevenZipBindPair(InIndex: 3, OutIndex: 3),
        ],
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Bcj2_СлишкомБольшойРазмерФинальногоВыхода_ВозвращаетNotSupported()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
      0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
      0xB0,                         // ord2 -> CopyB.in (global in=0)
      0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> CopyA.in (global in=5)
    ];

    // Здесь финальный выход ровно один: out1 у BCJ2.
    // Его размер специально делаем больше int.MaxValue,
    // чтобы попасть в верхнюю size-limit ветку уже после успешной раскладки BCJ2 input stream'ов.
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        packedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        bindPairs:
        [
          // BCJ2 slot0 (consumerIn=1) <- CopyA.out2
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),

          // BCJ2 slot2 (consumerIn=3) <- CopyC.out3
          new SevenZipBindPair(InIndex: 3, OutIndex: 3),

          // BCJ2 slot3 (consumerIn=4) <- CopyB.out0
          new SevenZipBindPair(InIndex: 4, OutIndex: 0),
        ],
        folderUnpackSizes: [1UL, ((ulong)int.MaxValue) + 1, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Bcj2_НетФинальногоВыхода_ВозвращаетNotSupported()
  {
    byte[] packedStreams =
    [
      0xC0, 0xC1, 0xC2,             // ord0 -> CopyC.in (global in=6)
    0xD0, 0xD1,                   // ord1 -> raw BCJ2 slot1 (global in=2)
    0xB0,                         // ord2 -> CopyB.in (global in=0)
    0xA0, 0xA1, 0xA2, 0xA3,       // ord3 -> CopyA.in (global in=5)
  ];

    // Первые три BindPair нужны, чтобы успешно собрать четыре входа BCJ2:
    //   slot0 <- CopyA.out2
    //   slot1 <- raw packed stream
    //   slot2 <- CopyC.out3
    //   slot3 <- CopyB.out0
    //
    // Последний BindPair синтетически помечает out1 самого BCJ2 как использованный.
    // В итоге использованы все out stream'ы folder'а: 0, 1, 2, 3.
    // Значит, финального output stream'а не остаётся.
    SevenZipStreamsInfo streamsInfo = CreateStreamsInfo(
        packedStreamIndices: [6UL, 2UL, 0UL, 5UL],
        bindPairs:
        [
          // BCJ2 slot0 (consumerIn=1) <- CopyA.out2
          new SevenZipBindPair(InIndex: 1, OutIndex: 2),

        // BCJ2 slot2 (consumerIn=3) <- CopyC.out3
        new SevenZipBindPair(InIndex: 3, OutIndex: 3),

        // BCJ2 slot3 (consumerIn=4) <- CopyB.out0
        new SevenZipBindPair(InIndex: 4, OutIndex: 0),

        // Помечаем out1 BCJ2 как использованный producer'ом.
        new SevenZipBindPair(InIndex: 5, OutIndex: 1),
        ],
        folderUnpackSizes: [1UL, 123UL, 4UL, 3UL]);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(output);
  }

  private static SevenZipStreamsInfo CreateStreamsInfo(
      ulong[] packedStreamIndices,
      SevenZipBindPair[] bindPairs,
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
        BindPairs: bindPairs,
        PackedStreamIndices: packedStreamIndices,
        NumInStreams: 7,
        NumOutStreams: 4);

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
