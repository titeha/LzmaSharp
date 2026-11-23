using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

public sealed class ZipReaderTests
{
  [Theory]
  [InlineData(CompressionLevel.Optimal)]
  [InlineData(CompressionLevel.NoCompression)]
  public void Read_ЧитаетZipСозданныйBcl(CompressionLevel level)
  {
    var files = new (string Name, byte[] Content)[]
    {
      ("hello.txt", Encoding.UTF8.GetBytes("Hello, ZIP reader!")),
      ("dir/repeated.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeat ", 2000)))),
      ("dir/sub/data.bin", MakePseudoRandom(5000, 7)),
      ("пример.txt", Encoding.UTF8.GetBytes("UTF-8 имя файла")),
    };

    byte[] zip = BclCreateZip(files, level);

    ZipReadResult result = ZipReader.Read(zip, out ZipEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, result);

    foreach ((string name, byte[] content) in files)
    {
      ZipEntry entry = Assert.Single(entries, e => e.Name == name && !e.IsDirectory);
      Assert.Equal(content, entry.Bytes);
    }
  }

  [Fact]
  public void Read_ПовреждённыйАрхив_НеПадаетНеобработанно()
  {
    byte[] zip = BclCreateZip([("a.txt", Encoding.UTF8.GetBytes("some content here"))], CompressionLevel.Optimal);
    zip[zip.Length / 2] ^= 0xFF;

    ZipReadResult result = ZipReader.Read(zip, out _);

    Assert.True(result is ZipReadResult.Ok or ZipReadResult.InvalidData or ZipReadResult.NotSupported);
  }

  [Fact]
  public void Read_НеZip_ВозвращаетInvalidData()
  {
    Assert.Equal(ZipReadResult.InvalidData, ZipReader.Read(new byte[100], out _));
  }

  // SEC-014: заплатка ложного uncompSize в запись центрального каталога (смещение +24 от её
  // начала). Локальный заголовок не трогаем: TryReadLocalData использует только compSize.
  private static byte[] PatchUncompSize(byte[] zip, uint uncompSize)
  {
    Span<byte> centralSignature = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(centralSignature, 0x02014b50);

    Span<byte> archive = zip;
    for (int i = 0; i + 4 <= archive.Length; i++)
    {
      if (centralSignature.SequenceEqual(archive.Slice(i, 4)))
      {
        BinaryPrimitives.WriteUInt32LittleEndian(archive.Slice(i + 24, 4), uncompSize);
        return zip;
      }
    }

    throw new InvalidOperationException("Запись центрального каталога не найдена.");
  }

  // SEC-017: заплатка по номеру элемента — ищет N-ю запись центрального каталога по
  // сигнатуре 0x02014b50 и пишет uint little-endian по смещению +24 (uncompSize элемента).
  private static byte[] PatchUncompSizeAt(byte[] zip, int entryIndex, uint uncompSize)
  {
    Span<byte> centralSignature = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(centralSignature, 0x02014b50);

    int found = -1;
    Span<byte> archive = zip;
    for (int i = 0; i + 4 <= archive.Length; i++)
    {
      if (centralSignature.SequenceEqual(archive.Slice(i, 4)))
      {
        found++;
        if (found == entryIndex)
        {
          BinaryPrimitives.WriteUInt32LittleEndian(archive.Slice(i + 24, 4), uncompSize);
          return zip;
        }
      }
    }

    throw new InvalidOperationException($"Запись центрального каталога номер {entryIndex} не найдена.");
  }

  // SEC-017: суммарный объём по заявленным размерам двух маленьких элементов с завышенным uncompSize.
  // Поэлементно каждый проходит (200 МиБ ≤ нижней планки 256 МиБ при крошечном compSize),
  // но их сумма 400 МиБ > предела операции max(256 МиБ, 1032 × packedTotal). До правки
  // первый элемент выделяет 200 МиБ и падает на сверке длины распаковки.
  [Fact]
  public void Read_СуммаЭлементов_ЗаПределомОперации_ОтказДоАлокации()
  {
    const uint twoHundredMb = (uint)(200L << 20);
    byte[] zip = BclCreateZip(
    [
      ("a.txt", new byte[64]),
      ("b.txt", new byte[64]),
    ], CompressionLevel.Optimal);
    zip = PatchUncompSizeAt(zip, 0, twoHundredMb);
    zip = PatchUncompSizeAt(zip, 1, twoHundredMb);

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = ZipReader.Read(zip, out _);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(ZipReadResult.NotSupported, res);
    Assert.True(
      after - before < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(after - before)} байт");
  }

  // SEC-017: предварительный проход не задевает обычные архивы. Несколько маленьких легитимных
  // элементов с суммарным распакованным объёмом много ниже нижней планки политики, поэтому
  // результат Ok и содержимое совпадает с исходным побайтово.
  [Fact]
  public void Read_МногоЛегитимныхЭлементов_ЧитаетсяКорректно()
  {
    byte[] Make(int seed)
    {
      var data = new byte[4 * 1024];
      for (int i = 0; i < data.Length; i++)
        data[i] = (byte)(seed + i);
      return data;
    }

    var files = new (string Name, byte[] Content)[]
    {
      ("one.txt", Make(1)),
      ("two.txt", Make(2)),
      ("three.txt", Make(3)),
      ("four.txt", Make(4)),
    };

    byte[] zip = BclCreateZip(files, CompressionLevel.Optimal);

    var res = ZipReader.Read(zip, out ZipEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, res);
    Assert.Equal(files.Length, entries.Length);
    foreach ((string name, byte[] content) in files)
    {
      ZipEntry entry = Assert.Single(entries, e => e.Name == name && !e.IsDirectory);
      Assert.Equal(content, entry.Bytes);
    }
  }

  // SEC-017: записи каталогов не участвуют в накоплении предварительного прохода (основной цикл
  // их пропускает), поэтому архив с записью каталога и одним файлом читается корректно. Принадлежность
  // к каталогу в проходе определяется по последнему байту имени без декодирования имени.
  [Fact]
  public void Read_ЗаписьКаталога_НеЛомаетУчёт()
  {
    byte[] fileContent = Encoding.UTF8.GetBytes("содержимое файла в архиве");
    var files = new (string Name, byte[] Content)[]
    {
      ("archive/", Array.Empty<byte>()),
      ("note.txt", fileContent),
    };

    byte[] zip = BclCreateZip(files, CompressionLevel.Optimal);

    var res = ZipReader.Read(zip, out ZipEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, res);

    Assert.Single(entries, e => e.Name == "archive/" && e.IsDirectory);
    ZipEntry fileEntry = Assert.Single(entries, e => e.Name == "note.txt" && !e.IsDirectory);
    Assert.Equal(fileContent, fileEntry.Bytes);
  }

  [Fact]
  public void Read_ОгромныйUncompSize_ЗаПределомInt_ОтказДоАлокации()
  {
    // uncompSize > int.MaxValue: до правки брал OverflowException наружу из публичного API.
    byte[] zip = BclCreateZip([("a.txt", new byte[4096])], CompressionLevel.Optimal);
    zip = PatchUncompSize(zip, 0xFFFFFFF0);

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = ZipReader.Read(zip, out _);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(ZipReadResult.NotSupported, res);
    Assert.True(
      after - before < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(after - before)} байт");
  }

  [Fact]
  public void Read_ОгромныйUncompSize_ВПределахInt_ОтказДоАлокации()
  {
    // uncompSize ~2 ГиБ больше CapBytes = 1 ГиБ при любом compSize.
    byte[] zip = BclCreateZip([("a.txt", new byte[4096])], CompressionLevel.Optimal);
    zip = PatchUncompSize(zip, 0x7FFFFFC0);

    long before = GC.GetAllocatedBytesForCurrentThread();
    var res = ZipReader.Read(zip, out _);
    long after = GC.GetAllocatedBytesForCurrentThread();

    Assert.Equal(ZipReadResult.NotSupported, res);
    Assert.True(
      after - before < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(after - before)} байт");
  }

  [Fact]
  public void Read_НаГраницеПолитики_256МиБПограничноНеNotSupported()
  {
    // Крошечный compSize => предел равен нижней планке FloorBytes = 256 МиБ.
    byte[] tinyContent = new byte[64];
    byte[] zipOver = BclCreateZip([("a.txt", tinyContent)], CompressionLevel.Optimal);
    zipOver = PatchUncompSize(zipOver, (uint)(256L << 20 | 1));

    long overBefore = GC.GetAllocatedBytesForCurrentThread();
    var resOver = ZipReader.Read(zipOver, out _);
    long overAfter = GC.GetAllocatedBytesForCurrentThread();

    // 256 МиБ + 1 > предела => NotSupported, отказ до выделения памяти.
    Assert.Equal(ZipReadResult.NotSupported, resOver);
    Assert.True(
      overAfter - overBefore < 1024 * 1024,
      $"Алокация превысила 1 МиБ: {(overAfter - overBefore)} байт");

    byte[] zipAt = BclCreateZip([("a.txt", tinyContent)], CompressionLevel.Optimal);
    zipAt = PatchUncompSize(zipAt, (uint)(256L << 20));

    // 256 МиБ == предела => проверка политикой пройдена, далее элемент упадёт по CRC/длине.
    // Утверждаем только код результата: для этого входа политика память выделяет (до 256 МиБ),
    // поэтому дельту аллокаций для него не утверждаем — см. отчёт. Гигабайты не выделяем.
    var resAt = ZipReader.Read(zipAt, out _);
    Assert.NotEqual(ZipReadResult.NotSupported, resAt);
  }

  [Fact]
  public void Read_ЛегитимныйUncompSize_НижеПолитики_ЧитаетсяКорректно()
  {
    byte[] content = new byte[64 * 1024];
    for (int i = 0; i < content.Length; i++)
      content[i] = (byte)(i % 17);
    byte[] zip = BclCreateZip([("a.txt", content)], CompressionLevel.Optimal);

    var res = ZipReader.Read(zip, out ZipEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, res);
    ZipEntry entry = Assert.Single(entries, e => !e.IsDirectory);
    Assert.Equal(content, entry.Bytes);
  }

  // SEC-014b: граница усиления Deflate по инварианту формата (1032:1). Все тесты — чистая
  // арифметика: метод политики возвращает предел без аллокации содержимого, сотни мегабайт
  // не выделяются. Существующие тесты SEC-014 (нижняя планка) этим не задеваются.
  [Fact]
  public void Политика_ГраницаФорматаDeflate_ФактИзПрогона()
  {
    // 260 МиБ нулей: compSize 265 029 Б, ratio 1028,7:1. 1032 * 265 029 = 273 509 928.
    // Нижняя (256 МиБ) и верхняя (1 ГиБ) планки не действуют.
    const long expected = 1032 * 265_029L; // 273 509 928

    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(265_029, 1032);

    Assert.Equal(expected, limit);
    Assert.Equal(273_509_928L, limit);
  }

  [Fact]
  public void Политика_СтароеИФорматноеУсиление_ЛегитимныйЭлементПроходит()
  {
    // Тот же compSize = 265 029: по умолчанию (1024) предел 271 389 696, по границе формата
    // (1032) — 273 509 928. Легитимный uncompSize 272 000 000 раньше получал отказ, теперь нет.
    long oldLimit = SevenZipMemoryPolicy.GetMaxUnpackBytes(265_029); // значение по умолчанию 1024
    long newLimit = SevenZipMemoryPolicy.GetMaxUnpackBytes(265_029, 1032);

    const long oldLimitExpected = 1024 * 265_029L; // 271 389 696
    const long legitUncomp = 272_000_000L;

    Assert.Equal(oldLimitExpected, oldLimit);
    Assert.Equal(newLimit, SevenZipMemoryPolicy.GetMaxUnpackBytes(265_029, 1032));
    Assert.True(legitUncomp > oldLimit, "Легитимный элемент раньше попадал под отказ 1024.");
    Assert.True(legitUncomp <= newLimit, "Легитимный элемент проходит по границе формата 1032.");
  }

  [Fact]
  public void Политика_НижняяПланка_ДействуетСФорматнымУсилением()
  {
    // compSize 64: 64 * 1032 = 66 048 ниже нижней планки, поэтому предел = Floor = 256 МиБ.
    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(64, 1032);

    Assert.Equal(256L << 20, limit);
  }

  [Fact]
  public void Политика_ВерхняяПланка_НасыщениеБезПереполнения()
  {
    // int.MaxValue при 1032 насыщается до Cap = 1 ГиБ, исключение не бросается.
    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(int.MaxValue, 1032);

    Assert.Equal(1L << 30, limit);
  }

  [Fact]
  public void Политика_ОтрицательнаяДлина_НеБросаетИсключение()
  {
    // Отрицательная длина приводит к нулю, далее действует нижняя планка.
    long limit = SevenZipMemoryPolicy.GetMaxUnpackBytes(-1, 1032);

    Assert.Equal(256L << 20, limit);
  }

  [Fact]
  public void Политика_ЗначениеПоУмолчанию_СохраняетПоведениеДля7z()
  {
    // Значение по умолчанию должно быть равно явному 1024 для каждого входа — вызовы 7z
    // в SevenZipFolderDecoder.cs не меняли бы поведения.
    long[] values = [0, 64, 1_048_575, 1_048_576, 265_029, (long)int.MaxValue];

    foreach (long x in values)
      Assert.Equal(SevenZipMemoryPolicy.GetMaxUnpackBytes(x, 1024), SevenZipMemoryPolicy.GetMaxUnpackBytes(x));
  }

  private static byte[] BclCreateZip((string Name, byte[] Content)[] files, CompressionLevel level)
  {
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach ((string name, byte[] content) in files)
      {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        using Stream s = entry.Open();
        s.Write(content, 0, content.Length);
      }
    }

    return ms.ToArray();
  }

  private static byte[] MakePseudoRandom(int length, int seed)
  {
    var random = new Random(seed);
    byte[] data = new byte[length];
    random.NextBytes(data);
    return data;
  }
}
