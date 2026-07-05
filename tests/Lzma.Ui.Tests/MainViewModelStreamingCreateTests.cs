using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты потокового создания архива из UI (LZMA2 + пикер со ссылками): архив пишется на диск потоком
/// через сервис, без чтения файлов в память, и корректно распаковывается.
/// </summary>
public sealed class MainViewModelStreamingCreateTests
{
  private sealed class StubArchivePicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  // Пикер, отдающий ссылки на файлы (streaming) из данных в памяти.
  private sealed class RefsSourceFilesPicker(IReadOnlyList<PickedFileRef> refs) : ISourceFilesPicker
  {
    public bool SupportsRefs => true;

    public Task<IReadOnlyList<PickedFileRef>?> PickFileRefsAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
      long bytes = 0;
      for (int i = 0; i < refs.Count; i++)
      {
        bytes += refs[i].Length;
        progress?.Report(new ScanProgress(i + 1, bytes));
      }

      return Task.FromResult<IReadOnlyList<PickedFileRef>?>(refs);
    }

    // Байтовый путь не должен вызываться при streaming.
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
        => throw new InvalidOperationException("должен использоваться потоковый путь");
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult(path);
  }

  [Fact]
  public async Task ПотоковоеСоздание_LZMA2_ПишетНаДискИРаспаковывается()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет-привет-привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("UI поток 0123456789 ", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiStreamingCreate", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string outPath = Path.Combine(dir, "out.7z");

    try
    {
      var refs = new List<PickedFileRef>
      {
        new("a.txt", a.LongLength, () => new MemoryStream(a)),
        new("big.bin", big.LongLength, () => new MemoryStream(big)),
      };

      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          new RefsSourceFilesPicker(refs),
          new StubSaveFilePicker(outPath));

      // Метод по умолчанию — LZMA2 → потоковый путь.
      Assert.Equal(SevenZipWriterCompressionMethod.Lzma2, vm.SelectedCompressionMethod);

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));
      Assert.Contains("стало", vm.StatusMessage); // итог с коэффициентом

      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

      Assert.Equal(2, entries.Length);
      Assert.Equal("a.txt", entries[0].Name);
      Assert.Equal(a, entries[0].Bytes);
      Assert.Equal("big.bin", entries[1].Name);
      Assert.Equal(big, entries[1].Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task НеLZMA2_ПотоковыйПуть_НеИспользуется()
  {
    // Для не-LZMA2 метода должен идти байтовый путь (RefsSourceFilesPicker.PickFilesAsync бросает),
    // поэтому выбор Copy приведёт к вызову байтового пикера → исключение подтверждает ветвление.
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiStreamingCreate2", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    try
    {
      var refs = new List<PickedFileRef> { new("a.txt", 3, () => new MemoryStream([1, 2, 3])) };

      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          new RefsSourceFilesPicker(refs),
          new StubSaveFilePicker(Path.Combine(dir, "out.7z")))
      {
        SelectedCompressionMethod = SevenZipWriterCompressionMethod.Copy,
      };

      await Assert.ThrowsAsync<InvalidOperationException>(() => vm.CreateCommand.ExecuteAsync());
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
