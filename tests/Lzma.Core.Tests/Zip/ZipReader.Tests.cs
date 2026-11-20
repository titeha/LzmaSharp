using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

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
