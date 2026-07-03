using System.Text;
using System.Threading;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Кооперативная отмена: writer (между файлами) и decoder (между folder-ами) проверяют
/// CancellationToken и бросают <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class SevenZipCancellationTests
{
  private static SevenZipArchiveWriterEntry[] TwoFiles()
  {
    byte[] a = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("данные ", 200)));
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("прочее ", 200)));
    return [new SevenZipArchiveWriterEntry("a.txt", a), new SevenZipArchiveWriterEntry("b.txt", b)];
  }

  [Fact]
  public void BuildArchive_ОтменённыйТокен_БросаетOperationCanceled()
  {
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.Throws<OperationCanceledException>(() =>
        SevenZipArchiveWriter.BuildArchive(
            TwoFiles(),
            SevenZipCompressionOptions.ForMethod(SevenZipWriterCompressionMethod.Lzma2),
            out _,
            progress: null,
            token: cts.Token));
  }

  [Fact]
  public void DecodeToEntries_ОтменённыйТокен_БросаетOperationCanceled()
  {
    // Сначала собираем корректный архив без отмены.
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        TwoFiles(), SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.Throws<OperationCanceledException>(() =>
        SevenZipArchiveDecoder.DecodeToEntries(
            archive, SevenZipDecodeOptions.Default, out _, out _, progress: null, token: cts.Token));
  }

  [Fact]
  public void BuildArchive_БезОтмены_РаботаетКакПрежде()
  {
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        TwoFiles(),
        SevenZipCompressionOptions.ForMethod(SevenZipWriterCompressionMethod.Lzma2),
        out byte[] archive,
        progress: null,
        token: default));

    Assert.NotEmpty(archive);
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(2, entries.Length);
  }
}
