using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты канала «текущий файл» потоковых writer-ов: имя + КОДЕК каждого непустого файла репортятся
/// по ходу (по разу на файл, каталоги не репортятся), кодек соответствует методу/выбору «Авто».
/// </summary>
public sealed class SevenZipArchiveWriterCurrentFileTests
{
  private sealed class CollectingProgress : IProgress<SevenZipCompressionFileProgress>
  {
    public readonly List<SevenZipCompressionFileProgress> Items = [];
    public void Report(SevenZipCompressionFileProgress value) => Items.Add(value);
    public List<string> Names => Items.ConvertAll(i => i.Name);
  }

  private static List<SevenZipStreamingEntry> ThreeFiles()
  {
    byte[] a = Encoding.UTF8.GetBytes("первый файл 12345");
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("второй ", 3000)));
    return new List<SevenZipStreamingEntry>
    {
      new("one.txt", a.LongLength, () => new MemoryStream(a)),
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true), // каталог — не репортится
      new("two.txt", b.LongLength, () => new MemoryStream(b)),
    };
  }

  [Fact]
  public void Lzma2_РепортитИменаИКодек()
  {
    var progress = new CollectingProgress();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(ThreeFiles(), ms, 1 << 20, 0, null, default, progress));

    Assert.Contains("one.txt", progress.Names);
    Assert.Contains("two.txt", progress.Names);
    Assert.DoesNotContain("dir", progress.Names);
    Assert.All(progress.Items, i => Assert.Equal("LZMA2", i.Codec));
  }

  [Fact]
  public void Ppmd_РепортитИменаВПорядке_КодекPpmd()
  {
    var progress = new CollectingProgress();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildPpmdArchiveToStream(ThreeFiles(), ms, null, default, progress));

    Assert.Equal(new[] { "one.txt", "two.txt" }, progress.Names);
    Assert.All(progress.Items, i => Assert.Equal("PPMd", i.Codec));
  }

  [Fact]
  public void Auto_РепортитКодекПофайлово()
  {
    // Текст → PPMd; случайное (высокая энтропия) → Copy.
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("слова и предложения. ", 4000)));
    var random = new byte[300_000];
    uint s = 0x13572468;
    for (int i = 0; i < random.Length; i++) { s = s * 1664525u + 1013904223u; random[i] = (byte)(s >> 24); }

    var entries = new List<SevenZipStreamingEntry>
    {
      new("doc.txt", text.LongLength, () => new MemoryStream(text)),
      new("blob.bin", random.LongLength, () => new MemoryStream(random)),
    };

    var progress = new CollectingProgress();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, ms, 1 << 20, null, default, progress));

    Assert.Equal("PPMd", progress.Items.Find(i => i.Name == "doc.txt").Codec);
    Assert.Equal("Copy", progress.Items.Find(i => i.Name == "blob.bin").Codec);
  }
}

/// <summary>Тесты канала «текущий файл» при ИЗВЛЕЧЕНИИ: имена файлов репортятся по ходу распаковки.</summary>
public sealed class SevenZipExtractCurrentFileTests
{
  private sealed class CollectingProgress : IProgress<string>
  {
    public readonly List<string> Names = [];
    public void Report(string value) => Names.Add(value);
  }

  [Fact]
  public void ExtractToDirectory_РепортитИменаФайлов()
  {
    byte[] a = Encoding.UTF8.GetBytes("файл один");
    byte[] b = Encoding.UTF8.GetBytes("файл два — подлиннее");

    var entries = new List<SevenZipStreamingEntry>
    {
      new("first.txt", a.LongLength, () => new MemoryStream(a)),
      new("second.txt", b.LongLength, () => new MemoryStream(b)),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, ms, 1 << 20));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaExtractCurFile", Guid.NewGuid().ToString("N"));
    try
    {
      var progress = new CollectingProgress();
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectory(ms.ToArray(), SevenZipDecodeOptions.Default, dir,
              overwrite: false, out _, null, default, progress));

      Assert.Contains("first.txt", progress.Names);
      Assert.Contains("second.txt", progress.Names);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
