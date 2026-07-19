using System.IO.Compression;
using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковый путь записи больших файлов (> порога): заголовок с заглушками → потоковое сжатие →
/// seek-назад патч CRC/compSize, при необходимости ZIP64-размеры в local/central. Пороги понижены,
/// чтобы прогнать ветки на маленьких файлах. Сверка нашим экстрактором И независимым BCL ZipArchive.
/// </summary>
public sealed class ZipStreamWriterLargePathTests
{
  private static ZipStreamingEntry Entry(string name, byte[] data)
      => new(name, data.Length, () => new MemoryStream(data, writable: false));

  private static ZipStreamingEntry Dir(string name)
      => new(name, 0, () => throw new InvalidOperationException(), IsDirectory: true);

  private static byte[] Write(IReadOnlyList<ZipStreamingEntry> entries, long largeThreshold, long zip64SizeThreshold)
  {
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok,
        ZipStreamWriter.Write(entries, ms, largeThreshold, zip64SizeThreshold, null, default, null, 0, null));
    return ms.ToArray();
  }

  private static void AssertBcl(byte[] archive, params (string Name, byte[] Data)[] expected)
  {
    using var ms = new MemoryStream(archive, writable: false);
    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
    foreach ((string name, byte[] data) in expected)
    {
      ZipArchiveEntry? e = zip.GetEntry(name);
      Assert.NotNull(e);
      using Stream s = e!.Open();
      using var outMs = new MemoryStream();
      s.CopyTo(outMs);
      Assert.Equal(data, outMs.ToArray());
    }
  }

  private static void AssertOurExtractor(byte[] archive, params (string Name, byte[] Data)[] expected)
  {
    string dest = Path.Combine(Path.GetTempPath(), "lzs-zw-large-" + Guid.NewGuid().ToString("N"));
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));
      Assert.Equal(ZipExtractResult.Ok, ZipStreamExtractor.ExtractToDirectory(ms, entries, dest));
      foreach ((string name, byte[] data) in expected)
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar))));
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  private static byte[] Compressible(int n)
      => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("data-line ", n / 10 + 1)).Substring(0, n));

  private static byte[] Incompressible(int n, int seed)
  {
    byte[] b = new byte[n];
    new Random(seed).NextBytes(b);
    return b;
  }

  [Fact]
  public void ПотоковыйПуть_Сжимаемый_БезZip64()
  {
    byte[] data = Compressible(5000);
    byte[] archive = Write([Entry("big/text.txt", data)], largeThreshold: 10, zip64SizeThreshold: long.MaxValue);

    AssertBcl(archive, ("big/text.txt", data));
    AssertOurExtractor(archive, ("big/text.txt", data));
  }

  [Fact]
  public void ПотоковыйПуть_Несжимаемый_БезZip64()
  {
    byte[] data = Incompressible(6000, 3);
    byte[] archive = Write([Entry("noise.bin", data)], largeThreshold: 10, zip64SizeThreshold: long.MaxValue);

    AssertBcl(archive, ("noise.bin", data));
    AssertOurExtractor(archive, ("noise.bin", data));
  }

  [Fact]
  public void ПотоковыйПуть_Zip64Размеры()
  {
    // Оба порога низкие → маленький файл идёт потоково И с резервом ZIP64-размеров в local/central.
    byte[] data = Compressible(4000);
    byte[] archive = Write([Entry("z64/data.txt", data)], largeThreshold: 10, zip64SizeThreshold: 10);

    AssertBcl(archive, ("z64/data.txt", data));
    AssertOurExtractor(archive, ("z64/data.txt", data));
  }

  [Fact]
  public void Смешанный_Директория_МелкийВолной_БольшойПотоком()
  {
    byte[] small = Compressible(200);   // ≤ порог → параллельная волна
    byte[] big = Compressible(5000);    // > порог → потоковый путь
    byte[] noise = Incompressible(4096, 9);

    byte[] archive = Write(
    [
        Dir("folder/"),
        Entry("folder/small.txt", small),
        Entry("folder/big.txt", big),
        Entry("root-noise.bin", noise),
    ], largeThreshold: 1000, zip64SizeThreshold: long.MaxValue);

    AssertBcl(archive, ("folder/small.txt", small), ("folder/big.txt", big), ("root-noise.bin", noise));
    AssertOurExtractor(archive, ("folder/small.txt", small), ("folder/big.txt", big), ("root-noise.bin", noise));
  }
}
