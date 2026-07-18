using System.IO.Compression;
using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковое извлечение ZIP из <see cref="Stream"/> на диск (без загрузки архива в память):
/// чтение каталога <see cref="ZipStreamReader"/> → <see cref="ZipStreamExtractor"/> (Store-копия,
/// Deflate через потоковый декодер, CRC на лету, безопасная запись и откат).
/// </summary>
public sealed class ZipStreamExtractorTests
{
  private static string NewTempDir()
      => Path.Combine(Path.GetTempPath(), "lzs-zipsx-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void Извлечение_ФайлыИПапки_StoreИDeflate()
  {
    byte[] text = Encoding.UTF8.GetBytes("Hello streaming zip extractor!");
    byte[] compressible = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeat ", 4000))); // → Deflate
    var rnd = new Random(1234);
    byte[] incompressible = new byte[3000];
    rnd.NextBytes(incompressible); // → Store

    ZipWriter.Build(
    [
        new ZipWriterEntry("dir/", [], IsDirectory: true),
        new ZipWriterEntry("dir/readme.txt", text),
        new ZipWriterEntry("dir/sub/big.txt", compressible),
        new ZipWriterEntry("noise.bin", incompressible),
    ], out byte[] archive);

    ExtractAndAssert(archive, dest =>
    {
      Assert.True(Directory.Exists(Path.Combine(dest, "dir")));
      Assert.Equal(text, File.ReadAllBytes(Path.Combine(dest, "dir", "readme.txt")));
      Assert.Equal(compressible, File.ReadAllBytes(Path.Combine(dest, "dir", "sub", "big.txt")));
      Assert.Equal(incompressible, File.ReadAllBytes(Path.Combine(dest, "noise.bin")));
    });
  }

  [Fact]
  public void Извлечение_БольшойDeflate_ПотоковыйInflate()
  {
    // Выход заведомо больше окна инфлейтера (128 КБ) — проверяем потоковый декод сквозь извлечение.
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 20_000)));
    Assert.True(big.Length > 300_000);

    ZipWriter.Build([new ZipWriterEntry("big/data.txt", big)], out byte[] archive);

    ExtractAndAssert(archive, dest =>
        Assert.Equal(big, File.ReadAllBytes(Path.Combine(dest, "big", "data.txt"))));
  }

  [Fact]
  public void Извлечение_АрхиваBcl_ЧитаетИРаспаковывает()
  {
    // Независимый источник: ZIP, собранный BCL (Deflate).
    byte[] payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("bcl payload line\n", 10_000)));

    using var msArchive = new MemoryStream();
    using (var zip = new ZipArchive(msArchive, ZipArchiveMode.Create, leaveOpen: true))
    {
      using Stream s = zip.CreateEntry("folder/payload.txt", CompressionLevel.Optimal).Open();
      s.Write(payload, 0, payload.Length);
    }

    ExtractAndAssert(msArchive.ToArray(), dest =>
        Assert.Equal(payload, File.ReadAllBytes(Path.Combine(dest, "folder", "payload.txt"))));
  }

  [Fact]
  public void Извлечение_ZipSlip_ОтклоняетсяИНичегоНеОстаётся()
  {
    ZipWriter.Build(
    [
        new ZipWriterEntry("ok.txt", Encoding.UTF8.GetBytes("safe")),
        new ZipWriterEntry("../evil.txt", Encoding.UTF8.GetBytes("escape")),
    ], out byte[] archive);

    string parent = NewTempDir();
    string dest = Path.Combine(parent, "out");
    Directory.CreateDirectory(parent);
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(ms, entries, dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.False(Directory.Exists(dest));                        // откат целевой папки
      Assert.False(File.Exists(Path.Combine(parent, "evil.txt"))); // побега наружу не случилось
    }
    finally
    {
      if (Directory.Exists(parent))
        Directory.Delete(parent, recursive: true);
    }
  }

  [Fact]
  public void Извлечение_ПорченыеДанные_InvalidDataИОткат()
  {
    // Первый член — несжимаемый (Store), знаем расположение его данных: 30 + len("a.bin").
    var rnd = new Random(99);
    byte[] noise = new byte[400];
    rnd.NextBytes(noise);
    ZipWriter.Build(
    [
        new ZipWriterEntry("a.bin", noise),
        new ZipWriterEntry("b.txt", Encoding.UTF8.GetBytes("second")),
    ], out byte[] archive);

    archive[30 + 5 + 10] ^= 0xFF; // портим байт в данных первого файла → CRC не сойдётся

    string dest = NewTempDir();
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

      ZipExtractResult result = ZipStreamExtractor.ExtractToDirectory(ms, entries, dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.False(Directory.Exists(dest)); // на диске ничего не осталось
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  // Читает каталог + извлекает поток на диск, прогоняет проверки и чистит папку.
  private static void ExtractAndAssert(byte[] archive, Action<string> assert)
  {
    string dest = NewTempDir();
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

      Assert.Equal(ZipExtractResult.Ok, ZipStreamExtractor.ExtractToDirectory(ms, entries, dest));

      assert(dest);
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }
}
