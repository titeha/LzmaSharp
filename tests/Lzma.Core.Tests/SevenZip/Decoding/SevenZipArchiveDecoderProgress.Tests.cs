using System.Collections.Generic;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderProgressTests
{
  private sealed class RecordingProgress : IProgress<SevenZipProgress>
  {
    public List<SevenZipProgress> Reports { get; } = [];
    public void Report(SevenZipProgress value) => Reports.Add(value);
  }

  [Fact]
  public void DecodeToEntries_СПрогрессом_ОтчётыМонотонныИДоходятДоИтога()
  {
    byte[] f1 = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("первый ", 300)));
    byte[] f2 = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("второй ", 700)));
    byte[] f3 = Encoding.UTF8.GetBytes("третий");

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", f1),
            new SevenZipArchiveWriterEntry("b.txt", f2),
            new SevenZipArchiveWriterEntry("c.txt", f3),
        ],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive));

    var progress = new RecordingProgress();

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeToEntries(
        archive, SevenZipDecodeOptions.Default, out SevenZipDecodedEntry[] entries, out _, progress));

    Assert.Equal(3, entries.Length);

    long total = f1.Length + f2.Length + f3.Length;

    Assert.NotEmpty(progress.Reports);

    // Все отчёты несут один и тот же общий размер.
    Assert.All(progress.Reports, p => Assert.Equal(total, p.TotalBytes));

    // Первый отчёт — стартовый (0), последний — полный (total).
    Assert.Equal(0, progress.Reports[0].BytesProcessed);
    Assert.Equal(total, progress.Reports[^1].BytesProcessed);

    // BytesProcessed не убывает.
    for (int i = 1; i < progress.Reports.Count; i++)
      Assert.True(progress.Reports[i].BytesProcessed >= progress.Reports[i - 1].BytesProcessed);
  }

  [Fact]
  public void DecodeToEntries_ОдинБольшойФайл_ЕстьПромежуточныйОтчётВнутриFolder()
  {
    // Один файл ~1 МБ в одном folder. Раньше прогресс репортился только на границе folder
    // (0 и total). Within-folder гранулярность даёт промежуточные отчёты по ходу декода LZMA2.
    var sb = new StringBuilder();
    for (int i = 0; sb.Length < 1_000_000; i++)
      sb.Append($"строка номер {i} с текстом для сжатия LZMA2; ");
    byte[] content = Encoding.UTF8.GetBytes(sb.ToString());

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("big.txt", content)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive));

    var progress = new RecordingProgress();

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeToEntries(
        archive, SevenZipDecodeOptions.Default, out SevenZipDecodedEntry[] entries, out _, progress));

    byte[] decoded = Assert.Single(entries).Bytes;
    Assert.Equal(content, decoded);

    long total = content.Length;
    Assert.All(progress.Reports, p => Assert.Equal(total, p.TotalBytes));
    Assert.Equal(0, progress.Reports[0].BytesProcessed);
    Assert.Equal(total, progress.Reports[^1].BytesProcessed);

    for (int i = 1; i < progress.Reports.Count; i++)
      Assert.True(progress.Reports[i].BytesProcessed >= progress.Reports[i - 1].BytesProcessed);

    // Главное доказательство within-folder: есть отчёт СТРОГО между 0 и total.
    Assert.Contains(progress.Reports, p => p.BytesProcessed > 0 && p.BytesProcessed < total);
  }

  [Fact]
  public void DecodeToEntries_БезПрогресса_РаботаетКакПрежде()
  {
    byte[] content = Encoding.UTF8.GetBytes("без прогресса");

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("x.txt", content)], SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeToEntries(
        archive, out SevenZipDecodedEntry[] entries));

    Assert.Equal(content, Assert.Single(entries).Bytes);
  }
}
