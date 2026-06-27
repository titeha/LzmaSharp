using System.Collections.Generic;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterProgressTests
{
  private sealed class RecordingProgress : IProgress<SevenZipProgress>
  {
    public List<SevenZipProgress> Reports { get; } = [];
    public void Report(SevenZipProgress value) => Reports.Add(value);
  }

  [Theory]
  [InlineData(SevenZipWriterCompressionMethod.Lzma2)]
  [InlineData(SevenZipWriterCompressionMethod.Ppmd)]
  [InlineData(SevenZipWriterCompressionMethod.Copy)]
  public void BuildArchive_СПрогрессом_ОтчётыДоходятДоОбъёмаИсходных(SevenZipWriterCompressionMethod method)
  {
    byte[] f1 = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("один ", 400)));
    byte[] f2 = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("два ", 800)));
    byte[] f3 = Encoding.UTF8.GetBytes("три");

    long total = f1.Length + f2.Length + f3.Length;

    var progress = new RecordingProgress();

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", f1),
            new SevenZipArchiveWriterEntry("b.txt", f2),
            new SevenZipArchiveWriterEntry("c.txt", f3),
        ],
        new SevenZipCompressionOptions { Method = method },
        out byte[] archive,
        progress));

    Assert.NotEmpty(progress.Reports);
    Assert.All(progress.Reports, p => Assert.Equal(total, p.TotalBytes));
    Assert.Equal(0, progress.Reports[0].BytesProcessed);
    Assert.Equal(total, progress.Reports[^1].BytesProcessed);

    for (int i = 1; i < progress.Reports.Count; i++)
      Assert.True(progress.Reports[i].BytesProcessed >= progress.Reports[i - 1].BytesProcessed);

    // Архив корректно распаковывается.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(3, entries.Length);
  }

  [Fact]
  public void BuildArchive_БезПрогресса_РаботаетКакПрежде()
  {
    byte[] content = Encoding.UTF8.GetBytes("без прогресса при создании");

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("x.txt", content)], SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(content, Assert.Single(entries).Bytes);
  }
}
