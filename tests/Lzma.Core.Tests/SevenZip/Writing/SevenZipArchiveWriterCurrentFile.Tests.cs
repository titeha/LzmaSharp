using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты канала «текущий файл» (IProgress&lt;string&gt;) потоковых writer-ов: имена сжимаемых файлов
/// репортятся по ходу — по одному разу на непустой файл, в порядке архива.
/// </summary>
public sealed class SevenZipArchiveWriterCurrentFileTests
{
  private sealed class CollectingProgress : IProgress<string>
  {
    public readonly List<string> Names = [];
    public void Report(string value) => Names.Add(value);
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
  public void Lzma2_РепортитИменаФайлов()
  {
    var progress = new CollectingProgress();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(ThreeFiles(), ms, 1 << 20, 0, null, default, progress));

    Assert.Contains("one.txt", progress.Names);
    Assert.Contains("two.txt", progress.Names);
    Assert.DoesNotContain("dir", progress.Names);
  }

  [Fact]
  public void Ppmd_РепортитИменаФайлов_ВПорядке()
  {
    var progress = new CollectingProgress();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildPpmdArchiveToStream(ThreeFiles(), ms, null, default, progress));

    Assert.Equal(new[] { "one.txt", "two.txt" }, progress.Names);
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
