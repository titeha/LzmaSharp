using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderUnsupportedTopologyTests
{
  [Fact]
  public void DecodeFolderToArray_TwoIndependentCopyCodersInOneFolder_NotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
                new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0, 1],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
                [3, 4],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3, 4]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3, 4, 5, 6, 7];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_OnePackedStream_TwoCoders_WithoutBindPairs_NotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
            new SevenZipCoderInfo([0x00], [], 1, 1),
                new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
                [3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_ПапкаБезCoder_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders: [],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 0,
        NumOutStreams: 0);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [0],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [0]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams: [],
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_КоличествоПотоковПапкиНеСовпадаетСЧисломCoder_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Theory]
  [InlineData(2, 1)]
  [InlineData(1, 2)]
  public void DecodeFolderToArray_CoderНе1In1Out_ВозвращаетNotSupported(
      int coderNumInStreams,
      int coderNumOutStreams)
  {
    // Важно: counts самой папки держим согласованными с coderCount,
    // чтобы попасть именно в guard на уровне отдельного coder-а.
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo(
            [0x00],
            [],
            (ulong)coderNumInStreams,
            (ulong)coderNumOutStreams),
        ],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_СтартовыйCoderНеПокрываетОтдельныйЦикл_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          // Отдельный цикл: 1 -> 0 и 0 -> 1.
          new SevenZipBindPair(InIndex: 0, OutIndex: 1),
        new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 3,
        NumOutStreams: 3);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Theory]
  [InlineData(2, 0)]
  [InlineData(1, 2)]
  public void DecodeFolderToArray_BindPairИндексВыходитЗаДиапазонCoder_ВозвращаетInvalidData(
    ulong inIndex,
    ulong outIndex)
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: inIndex, OutIndex: outIndex),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_BindPairЗамыкаетсяНаСебя_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 0, OutIndex: 0),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_ОдинConsumerИмеетДваИсточника_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        new SevenZipBindPair(InIndex: 1, OutIndex: 2),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 3,
        NumOutStreams: 3);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_ОдинProducerРазветвляетсяНаДвухConsumer_ВозвращаетInvalidData()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        new SevenZipBindPair(InIndex: 2, OutIndex: 0),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 3,
        NumOutStreams: 3);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_ПустыеPackedStreamIndices_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs: [],
        PackedStreamIndices: [],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [0],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: []);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams: [],
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_СлишкомМногоBindPairsДляЛинейнойПапки_ВозвращаетNotSupported()
  {
    var folder = new SevenZipFolder(
        Coders:
        [
          new SevenZipCoderInfo([0x00], [], 1, 1),
        new SevenZipCoderInfo([0x00], [], 1, 1),
        ],
        BindPairs:
        [
          new SevenZipBindPair(InIndex: 1, OutIndex: 0),
        new SevenZipBindPair(InIndex: 0, OutIndex: 1),
        ],
        PackedStreamIndices: [0],
        NumInStreams: 2,
        NumOutStreams: 2);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes:
        [
          [3, 3],
        ]);

    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [3]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    byte[] packedStreams = [1, 2, 3];

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, r);
    Assert.Empty(output);
  }
}
