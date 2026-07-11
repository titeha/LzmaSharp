using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты извлечения архива из Stream/файла (SevenZipArchiveDecoder.ExtractToDirectoryFromStream): архив
/// НЕ загружается в память, folder-ы декодируются потоком по смещению → распаковка архивов больше 2 ГиБ.
/// </summary>
public sealed class SevenZipArchiveDecoderExtractFromStreamTests
{
  private static string TempDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaExtractFromStream", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static void TryDeleteTree(string dir)
  {
    try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
  }

  private static string BuildArchiveFile(string dir, IReadOnlyList<SevenZipStreamingEntry> entries)
  {
    string archivePath = Path.Combine(dir, "in.7z");
    using var fs = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite);
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, fs, 1 << 20));
    return archivePath;
  }

  [Fact]
  public void ИзвлекаетИзФайла_МногоФайлов_Вложенность_Пустые_БайтВБайт()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Из файла-архива 0123456789 ", 8000)));
    byte[] b = Encoding.UTF8.GetBytes("мир");

    var entries = new List<SevenZipStreamingEntry>
    {
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
      new("a.txt", a.LongLength, () => new MemoryStream(a)),
      new("empty.txt", 0, () => new MemoryStream([])),
      new("dir/big.bin", big.LongLength, () => new MemoryStream(big)),
      new("b.txt", b.LongLength, () => new MemoryStream(b)),
    };

    string dir = TempDir();
    try
    {
      string archivePath = BuildArchiveFile(dir, entries);
      string outDir = Path.Combine(dir, "out");

      using (var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
      {
        Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, SevenZipDecodeOptions.Default, outDir, overwrite: false));
      }

      Assert.True(Directory.Exists(Path.Combine(outDir, "dir")));
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(outDir, "a.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(outDir, "b.txt")));
      Assert.Equal(big, File.ReadAllBytes(Path.Combine(outDir, "dir", "big.bin")));
      Assert.True(File.Exists(Path.Combine(outDir, "empty.txt")));
      Assert.Empty(File.ReadAllBytes(Path.Combine(outDir, "empty.txt")));
    }
    finally
    {
      TryDeleteTree(dir);
    }
  }

  [Fact]
  public void СовпадаетСоSpanИзвлечением()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("сверка extract 0123 ", 6000)));
    var entries = new List<SevenZipStreamingEntry>
    {
      new("x.bin", data.LongLength, () => new MemoryStream(data)),
    };

    string dir = TempDir();
    try
    {
      string archivePath = BuildArchiveFile(dir, entries);
      byte[] archiveBytes = File.ReadAllBytes(archivePath);

      // span-извлечение (эталон).
      string spanDir = Path.Combine(dir, "span");
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectory(
          archiveBytes, SevenZipDecodeOptions.Default, spanDir, overwrite: false, out _));

      // stream-извлечение.
      string streamDir = Path.Combine(dir, "stream");
      using (var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
      {
        Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, SevenZipDecodeOptions.Default, streamDir, overwrite: false));
      }

      Assert.Equal(
          File.ReadAllBytes(Path.Combine(spanDir, "x.bin")),
          File.ReadAllBytes(Path.Combine(streamDir, "x.bin")));
      Assert.Equal(data, File.ReadAllBytes(Path.Combine(streamDir, "x.bin")));
    }
    finally
    {
      TryDeleteTree(dir);
    }
  }

  [Fact]
  public void ПорченыйАрхив_InvalidData_ИНичегоНеОстаётся()
  {
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("целостность 0123456789 ", 8000)));
    var entries = new List<SevenZipStreamingEntry>
    {
      new("big.bin", big.LongLength, () => new MemoryStream(big)),
    };

    string dir = TempDir();
    try
    {
      string archivePath = BuildArchiveFile(dir, entries);

      // Портим байт в packed-области (после 32-байтной сигнатуры).
      byte[] bytes = File.ReadAllBytes(archivePath);
      bytes[40] ^= 0xFF;
      File.WriteAllBytes(archivePath, bytes);

      string outDir = Path.Combine(dir, "out");
      SevenZipArchiveDecodeResult r;
      using (var archive = new FileStream(archivePath, FileMode.Open, FileAccess.Read))
      {
        r = SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, SevenZipDecodeOptions.Default, outDir, overwrite: false);
      }

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.False(File.Exists(Path.Combine(outDir, "big.bin"))); // откат
      Assert.False(Directory.Exists(outDir));                     // целевая папка тоже
    }
    finally
    {
      TryDeleteTree(dir);
    }
  }
}
