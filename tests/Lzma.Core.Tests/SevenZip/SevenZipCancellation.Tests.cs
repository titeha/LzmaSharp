using System;
using System.Text;
using System.Threading;

using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
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

  // --- Per-chunk отмена: один большой файл прерывается ПОСРЕДИ, а не только на границе файла/folder ---

  // Крупный (многочанковый) сжимаемый вход: повтор → много LZMA2-чанков по 64 КБ.
  private static byte[] LargeCompressible()
      => Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("Ляляля повтор данных 0123456789 ", 40000)));

  [Fact]
  public void Lzma2Encode_ОтменённыйТокен_БросаетВнутриЧанкЦикла()
  {
    // У Lzma2LzmaEncoder.Encode нет пофайловой проверки — единственная точка отмены
    // это per-chunk (FlushChunk). Значит бросок доказывает, что токен доходит в чанк-цикл.
    var props = new LzmaProperties(3, 0, 2);
    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.Throws<OperationCanceledException>(() =>
        Lzma2LzmaEncoder.Encode(LargeCompressible(), props, dictionarySize: 1 << 20, token: cts.Token));
  }

  [Fact]
  public void Lzma2Decode_ОтменённыйТокен_БросаетВнутриЧанкЦикла()
  {
    var props = new LzmaProperties(3, 0, 2);
    byte[] stream = Lzma2LzmaEncoder.Encode(LargeCompressible(), props, dictionarySize: 1 << 20);

    using var cts = new CancellationTokenSource();
    cts.Cancel();

    Assert.Throws<OperationCanceledException>(() =>
        Lzma2Decoder.DecodeToArray(stream, 1 << 20, out _, out _, progress: null, token: cts.Token));
  }

  // Синхронный IProgress из делегата — колбэк выполняется на потоке декодера.
  private sealed class DelegateProgress<T>(Action<T> report) : IProgress<T>
  {
    public void Report(T value) => report(value);
  }

  [Fact]
  public void DecodeToEntries_ОтменаПосредиБольшогоФайла_БросаетOperationCanceled()
  {
    // Один большой файл = один folder. Раньше отмена ловилась только на границе folder-а,
    // теперь — по ходу (per-chunk). Отменяем из прогресс-колбэка на первом промежуточном
    // отчёте (0 < обработано < всего) — следующая итерация чанк-цикла декодера бросит OCE.
    var entry = new SevenZipArchiveWriterEntry("big.txt", LargeCompressible());
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [entry], SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    using var cts = new CancellationTokenSource();
    bool sawMidProgress = false;
    var progress = new DelegateProgress<SevenZipProgress>(p =>
    {
      if (p.TotalBytes > 0 && p.BytesProcessed > 0 && p.BytesProcessed < p.TotalBytes)
      {
        sawMidProgress = true;
        cts.Cancel();
      }
    });

    Assert.Throws<OperationCanceledException>(() =>
        SevenZipArchiveDecoder.DecodeToEntries(
            archive, SevenZipDecodeOptions.Default, out _, out _, progress: progress, token: cts.Token));

    Assert.True(sawMidProgress, "ожидался промежуточный (within-folder) отчёт прогресса до отмены");
  }
}
