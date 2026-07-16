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
