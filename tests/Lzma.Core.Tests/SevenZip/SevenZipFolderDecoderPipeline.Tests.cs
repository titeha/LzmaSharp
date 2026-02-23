using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderPipelineTests
{
  [Fact]
  public void DecodeFolderToArray_ThreeCoders_LinearPipeline_UsesBindPairsOrder()
  {
    // Исходные данные (pack stream == "сырой" поток, т.к. в тесте цепочка фильтров).
    byte[] input = [0, 1, 2, 3, 4, 5, 6, 7];

    // Цепочка: Copy -> Swap2 -> Swap4.
    // Важный момент теста: массив coders намеренно НЕ в порядке цепочки.
    // Coders: [Swap4 (0), Copy (1), Swap2 (2)]
    // BindPairs:
    //   Copy(1).out -> Swap2(2).in  => InIndex=2, OutIndex=1
    //   Swap2(2).out -> Swap4(0).in => InIndex=0, OutIndex=2
    byte[] expected = [2, 3, 0, 1, 6, 7, 4, 5];

    var packInfo = new SevenZipPackInfo(
      packPos: 0,
      packSizes: [(ulong)input.Length]);

    var coderSwap4 = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x04], // Swap4
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var coderCopy = new SevenZipCoderInfo(
      methodId: [0x00], // Copy
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var coderSwap2 = new SevenZipCoderInfo(
      methodId: [0x02, 0x03, 0x02], // Swap2
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coderSwap4, coderCopy, coderSwap2],
      BindPairs:
      [
        new SevenZipBindPair(InIndex: 2, OutIndex: 1),
        new SevenZipBindPair(InIndex: 0, OutIndex: 2),
      ],
      PackedStreamIndices: [0],
      NumInStreams: 3,
      NumOutStreams: 3);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes:
      [
        [
          (ulong)input.Length, // out stream 0 (Swap4)
          (ulong)input.Length, // out stream 1 (Copy)
          (ulong)input.Length, // out stream 2 (Swap2)
        ]
      ]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo: streamsInfo,
      packedStreams: input,
      folderIndex: 0,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r);
    Assert.Equal(expected, output);
  }
}
