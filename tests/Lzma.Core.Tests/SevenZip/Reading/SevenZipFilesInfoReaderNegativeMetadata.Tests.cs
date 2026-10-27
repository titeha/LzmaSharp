using System.Collections.Generic;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderNegativeMetadataTests
{
  [Fact]
  public void TryRead_ДублирующийсяCrc_ЭтоInvalidData()
  {
    byte[] crcPayload =
    [
      0x01,
      0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc, crcPayload),
      Property(SevenZipNid.Crc, crcPayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_CrcСНекорректнымAllAreDefined_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc,
      [
        0x02,
        0x44, 0x33, 0x22, 0x11,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_CrcСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc,
      [
        0x01,
        0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяMTime_ЭтоInvalidData()
  {
    byte[] mTimePayload =
    [
      0x01,
      0x00,
      0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime, mTimePayload),
      Property(SevenZipNid.MTime, mTimePayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeExternalData_ЭтоNotSupported()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x01,
        0x01,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeСНекорректнымAllAreDefined_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x02,
        0x00,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x01,
        0x00,
        0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_WinAttribExternalData_ЭтоNotSupported()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib,
      [
        0x01,
        0x01,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяWinAttrib_ЭтоInvalidData()
  {
    byte[] winAttribPayload =
    [
      0x01,
      0x00,
      0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib, winAttribPayload),
      Property(SevenZipNid.WinAttrib, winAttribPayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_WinAttribСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib,
      [
        0x01,
        0x00,
        0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_Имена_ОгромныйСчётчик_КороткийХвост_НеАллоцирует()
  {
    // Name: external=0 + один UTF-16 символ «A» без нулевого терминатора. Заявлено
    // 8_000_000 имён, но хвост описывает меньше. До проверки §5.1 выделяется
    // string[8_000_000] (~64 МиБ); 64 МиБ доказывают дефект, но не роняют машину.
    byte[] bytes = BuildFilesInfoVarCount(
      8_000_000UL,
      Property(SevenZipNid.Name, [0x00, 0x41, 0x00]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_Crc_ОгромныйСчётчик_Неопределённые_НеАллоцирует()
  {
    // Crc: AllAreDefined=0, хвост [0x00] — битовый вектор не описывает и 1/8 файла. До
    // проверки §5.2 выделяется bool[64_000_000] (~64 МиБ). 64 МиБ доказывают дефект, но
    // не роняют машину.
    byte[] bytes = BuildFilesInfoVarCount(
      64_000_000UL,
      Property(SevenZipNid.Crc, [0x00]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_MTime_ОгромныйСчётчик_Неопределённые_НеАллоцирует()
  {
    // MTime: AllAreDefined=0, хвост [0x00,0x00] — проходит раннюю проверку payload.Length < 2,
    // но битовый вектор не описывает и 1/8 файла. До проверки §5.3 выделяется
    // bool[64_000_000] (~64 МиБ).
    byte[] bytes = BuildFilesInfoVarCount(
      64_000_000UL,
      Property(SevenZipNid.MTime, [0x00, 0x00]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_MTime_ОгромныйСчётчик_ВнешниеДанные_НеАллоцирует_ИСохраняетNotSupported()
  {
    // MTime: AllAreDefined=1, External=1. По контракту №4 код — NotSupported (защита приоритета).
    // До проверки §5.3 всё равно выделяется bool[64_000_000] (~64 МиБ); после — без аллокации.
    byte[] bytes = BuildFilesInfoVarCount(
      64_000_000UL,
      Property(SevenZipNid.MTime, [0x01, 0x01]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_WinAttrib_ОгромныйСчётчик_Неопределённые_НеАллоцирует()
  {
    // WinAttrib: AllAreDefined=0, хвост [0x00,0x00] — проходит раннюю проверку payload.Length < 2,
    // но битовый вектор не описывает и 1/8 файла. До проверки §5.4 выделяется
    // bool[64_000_000] (~64 МиБ).
    byte[] bytes = BuildFilesInfoVarCount(
      64_000_000UL,
      Property(SevenZipNid.WinAttrib, [0x00, 0x00]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_WinAttrib_ОгромныйСчётчик_ВнешниеДанные_НеАллоцирует_ИСохраняетNotSupported()
  {
    // WinAttrib: AllAreDefined=1, External=1. По контракту №6 код — NotSupported (защита
    // приоритета). До проверки §5.4 всё равно выделяется bool[64_000_000] (~64 МиБ);
    // после — без аллокации.
    byte[] bytes = BuildFilesInfoVarCount(
      64_000_000UL,
      Property(SevenZipNid.WinAttrib, [0x01, 0x01]));

    long before = GC.GetAllocatedBytesForCurrentThread();
    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
    long deltaAlloc = after - before;
    Assert.True(deltaAlloc < 1_048_576, $"Аллокация {deltaAlloc} байт превышает допустимый предел 1 048 576");
  }

  [Fact]
  public void TryRead_Имена_РовноНаГранице_НеОтклоняется()
  {
    // fileCount=2, nameBytes = 4 нуля = 2 пустые имена. nameBytes.Length = 4 = 2*fileCount
    // ровно, условие «не больше» проходит на границе. Граничный тест: Ok и до, и после fixes.
    byte[] bytes = BuildFilesInfoVarCount(
      2UL,
      Property(SevenZipNid.Name, [0x00, 0x00, 0x00, 0x00, 0x00]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out var filesInfo, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);
    Assert.Equal(new[] { "", "" }, filesInfo.Names);
  }

  [Fact]
  public void TryRead_Crc_РовноНаГранице_НеОтклоняется()
  {
    // fileCount=1, AllAreDefined=1 + одно значение: длина payload = 1 + 4*1 ровно. Граничный
    // тест: Ok и до, и после fixes.
    byte[] bytes = BuildFilesInfoVarCount(
      1UL,
      Property(SevenZipNid.Crc, [0x01, 0x44, 0x33, 0x22, 0x11]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);
  }

  [Fact]
  public void TryRead_Имена_НаЭлементБольшеГранице_ЭтоInvalidData()
  {
    // fileCount=3 при тех же 4 байтах nameBytes: 3*2=6 > 4, проверка §5.1 отклоняет. Зелёный
    // до и после проверки; доказывает, что в условии нет лишнего запаса.
    byte[] bytes = BuildFilesInfoVarCount(
      3UL,
      Property(SevenZipNid.Name, [0x00, 0x00, 0x00, 0x00, 0x00]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  private static byte[] BuildFilesInfo(byte fileCount, params byte[][] properties)
  {
    var bytes = new List<byte>
    {
      SevenZipNid.FilesInfo,
      fileCount,
    };

    foreach (byte[] property in properties)
      bytes.AddRange(property);

    bytes.Add(SevenZipNid.End);
    return [.. bytes];
  }

  private static byte[] Property(byte nid, params byte[] payload)
  {
    var bytes = new List<byte>
    {
      nid,
      checked((byte)payload.Length),
    };

    bytes.AddRange(payload);
    return [.. bytes];
  }

  private static byte[] BuildFilesInfoVarCount(ulong fileCount, params byte[][] properties)
  {
    var bytes = new List<byte> { SevenZipNid.FilesInfo };
    SevenZipEncodedUInt64.TryWrite(bytes, fileCount, out _);
    foreach (byte[] property in properties)
      bytes.AddRange(property);
    bytes.Add(SevenZipNid.End);
    return [.. bytes];
  }
}
