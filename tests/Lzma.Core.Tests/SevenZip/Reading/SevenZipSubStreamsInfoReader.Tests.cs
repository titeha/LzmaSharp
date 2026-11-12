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

    Assert.NotNull(sub.UnpackCrcDefinedPerFolder);
    Assert.NotNull(sub.UnpackCrcPerFolder);

    Assert.Equal([true], sub.UnpackCrcDefinedPerFolder![0]);
    Assert.Equal([0x11223344u], sub.UnpackCrcPerFolder![0]);

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

    Assert.NotNull(sub.UnpackCrcDefinedPerFolder);
    Assert.NotNull(sub.UnpackCrcPerFolder);

    Assert.Equal([true, false, true], sub.UnpackCrcDefinedPerFolder![0]);
    Assert.Equal([0x44332211u, 0u, 0x88776655u], sub.UnpackCrcPerFolder![0]);
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
      folderCrcDefined: [true],
      folderCrc: [0x11223344u]);

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

    Assert.NotNull(sub.UnpackCrcDefinedPerFolder);
    Assert.NotNull(sub.UnpackCrcPerFolder);

    Assert.Equal([true], sub.UnpackCrcDefinedPerFolder![0]);
    Assert.Equal([0x11223344u], sub.UnpackCrcPerFolder![0]);
  }

  [Fact]
  public void TryRead_Crc_MultiFolder_SkipsSingleStreamFolderWithFolderCrc()
  {
    var coder = new SevenZipCoderInfo(methodId: [0x21], properties: [], numInStreams: 1, numOutStreams: 1);

    var folder0 = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var folder1 = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [1],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder0, folder1],
      folderUnpackSizes: [[10UL], [20UL]],
      folderCrcDefined: [true, false],
      folderCrc: [0x11111111u, 0u]);

    // folder0: n=1 (по умолчанию), folder1: n=2
    // Size: для folder1 читаем 1 размер (5), второй будет 15
    // CRC: unknown streams = 2 (только folder1), AllAreDefined=1 => 2 CRC значения
    byte[] src =
    [
      SevenZipNid.SubStreamsInfo,

    SevenZipNid.NumUnpackStream,
    0x01, // folder0
    0x02, // folder1

    SevenZipNid.Size,
    0x05, // folder1 stream0

    SevenZipNid.Crc,
    0x01, // AllAreDefined=1
    0xDD, 0xCC, 0xBB, 0xAA, // 0xAABBCCDD
    0x04, 0x03, 0x02, 0x01, // 0x01020304

    SevenZipNid.End,
  ];

    var result = SevenZipSubStreamsInfoReader.TryRead(src, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.Ok, result);
    Assert.Equal(src.Length, bytesConsumed);
    Assert.NotNull(sub);

    Assert.Equal([1UL, 2UL], sub!.NumUnpackStreamsPerFolder);
    Assert.Equal([10UL], sub.UnpackSizesPerFolder[0]);
    Assert.Equal([5UL, 15UL], sub.UnpackSizesPerFolder[1]);

    Assert.NotNull(sub.UnpackCrcDefinedPerFolder);
    Assert.NotNull(sub.UnpackCrcPerFolder);

    Assert.Equal([true], sub.UnpackCrcDefinedPerFolder![0]);
    Assert.Equal([0x11111111u], sub.UnpackCrcPerFolder![0]);

    Assert.Equal([true, true], sub.UnpackCrcDefinedPerFolder![1]);
    Assert.Equal([0xAABBCCDDu, 0x01020304u], sub.UnpackCrcPerFolder![1]);
  }

  [Fact]
  public void TryRead_Читает_БольшоеЧислоПотоков_НоНехваткаВхода_НеАллоцирует()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    // NumUnpackStreams = 8_000_000, Size указан, но ни одного байта размеров и без End.
    // 64 МиБ доказывают дефект, но не роняют машину (значение выбрано намеренно).
    var src = new List<byte>
    {
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
    };
    SevenZipEncodedUInt64.TryWrite(src, 8_000_000UL, out _);
    src.Add(SevenZipNid.Size);

    byte[] srcBytes = src.ToArray();

    long before = GC.GetAllocatedBytesForCurrentThread();
    var result = SevenZipSubStreamsInfoReader.TryRead(srcBytes, unpackInfo, out var sub, out var bytesConsumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_Читает_ПределЧислоПотоков_НоНеваткаВхода_НеАллоцирует()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    // NumUnpackStreams = int.MaxValue, Size указан, но ни одного байта размеров. Это граничное
    // значение из контракта: без проверки код попытался выделить ulong[int.MaxValue] (~16 ГиБ).
    // RED намеренно не воспроизводился — доказательство даёт тот же путь кода в тесте выше.
    var src = new List<byte>
    {
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.NumUnpackStream,
    };
    SevenZipEncodedUInt64.TryWrite(src, (ulong)int.MaxValue, out _);
    src.Add(SevenZipNid.Size);

    byte[] srcBytes = src.ToArray();

    var result = SevenZipSubStreamsInfoReader.TryRead(srcBytes, unpackInfo, out var sub, out var bytesConsumed);

    Assert.Equal(SevenZipSubStreamsInfoReadResult.NeedMoreInput, result);
    Assert.Equal(0, bytesConsumed);
    Assert.Null(sub);
  }

  [Fact]
  public void TryRead_ПолныйВход_Читает_NumUnpackStream_И_Size_И_Вычисляет_ПоследнийРазмер()
  {
    var unpackInfo = CreateUnpackInfo(folderUnpackSize: 10);

    // Тот же вход, что и в TryRead_Читает_NumUnpackStream_И_Size_И_Вычисляет_ПоследнийРазмер,
    // но как отдельный позитивный контроль: num=3 и ровно два размера (по одному байту каждый)
    // при полном входе. Проверка предела не должна срабатывать, когда вход достаточен
    // (streams - 1 = 2 == числу доступных байтов размеров).
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
