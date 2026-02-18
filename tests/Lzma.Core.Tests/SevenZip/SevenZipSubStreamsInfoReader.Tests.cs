using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipSubStreamsInfoReaderTests
{
  [Fact]
  public void TryRead_МинимальныйSubStreamsInfo_ПоУмолчаниюОдинПотокНаПапку()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.End,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);
    Assert.Equal([1UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([10UL], sub.UnpackSizesPerFolder[0]);
  }

  [Fact]
  public void TryRead_Читает_NumUnpackStream_И_Size_И_Вычисляет_ПоследнийРазмер()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    // NumUnpackStreams = 3
    // Sizes: 2, 3, (остальное 5)
    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x03,
      SevenZipNid.Size,
      0x02,
      0x03,
      SevenZipNid.End,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);

    Assert.Equal([3UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([2UL, 3UL, 5UL], sub.UnpackSizesPerFolder[0]);
  }

  [Fact]
  public void TryRead_Возвращает_NeedMoreInput_ЕслиДанныхНедостаточно()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      // дальше не хватает данных
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Возвращает_NeedMoreInput_ЕслиCrcОбрезан()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);
    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
    SevenZipNid.Crc,
    0x00, // AllAreDefined=0, но дальше должен быть Defined bitfield
  ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Читает_Crc_AllAreDefined_И_Возвращает_Ok()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);
    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
    SevenZipNid.Crc,
    0x01,                   // AllAreDefined = 1
    0x44, 0x33, 0x22, 0x11,  // CRC (1 поток)
    SevenZipNid.End,
  ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);
    Assert.Equal([1UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([10UL], sub.UnpackSizesPerFolder[0]);
  }

  [Fact]
  public void TryRead_Читает_Crc_PartialDefined_ДляНесколькихSubStreams()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    // NumUnpackStreams = 3, Sizes: 2, 3, (остаток 5)
    // CRC: Defined = [true, false, true] => 0xA0, CRCs для 2 defined => 8 байт.
    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
    SevenZipNid.NumUnpackStream,
    0x03,
    SevenZipNid.Size,
    0x02,
    0x03,

    SevenZipNid.Crc,
    0x00, // AllAreDefined = 0
    0xA0, // Defined bitfield
    0x11, 0x22, 0x33, 0x44,
    0x55, 0x66, 0x77, 0x88,

    SevenZipNid.End,
  ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);
    Assert.Equal([3UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([2UL, 3UL, 5UL], sub.UnpackSizesPerFolder[0]);
  }

  [Fact]
  public void TryRead_Возвращает_InvalidData_ЕслиСуммаРазмеровБольшеЧемПапка()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 4);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
      0x03,
      SevenZipNid.Size,
      0x02,
      0x03, // 2 + 3 > 4
      SevenZipNid.End,
    ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_Crc_UnknownCountIsZero_ЕслиFolderHasCrc()
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21],
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[10UL]],
      folderCrcDefined: [true]);

    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,
    SevenZipNid.Crc,
    0x01, // AllAreDefined=1, но numStreams=0 => CRC bytes отсутствуют
    SevenZipNid.End,
  ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);
    Assert.Equal([1UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([10UL], sub.UnpackSizesPerFolder[0]);
  }

  private static SevenZipUnpackInfo CreateUnpackInfo(ulong folderUnpackSize)
  {
    var coder = new SevenZipCoderInfo(
      methodId: [0x21], // LZMA2 (для теста это не важно)
      properties: [],
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    return new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[folderUnpackSize]]);
  }
}
