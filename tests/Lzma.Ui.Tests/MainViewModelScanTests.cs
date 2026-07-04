using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты живого счётчика на фазе сканирования/чтения файлов перед сжатием: форматирование
/// («N файлов, X МБ» со склонением) и поведение VM (индикатор зажигается по ходу и гаснет после).
/// </summary>
public sealed class MainViewModelScanTests
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

  // Пикер, который репортит скан-прогресс по мере «чтения» (синхронно, до возврата).
  private sealed class ReportingSourceFilesPicker(IReadOnlyList<PickedFile> files) : ISourceFilesPicker
  {
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
      long bytes = 0;
      for (int i = 0; i < files.Count; i++)
      {
        token.ThrowIfCancellationRequested();
        bytes += files[i].Bytes.LongLength;
        progress?.Report(new ScanProgress(i + 1, bytes));
      }

      return Task.FromResult<IReadOnlyList<PickedFile>?>(files);
    }
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult(path);
  }

  [Theory]
  [InlineData(1, "файл")]
  [InlineData(2, "файла")]
  [InlineData(4, "файла")]
  [InlineData(5, "файлов")]
  [InlineData(11, "файлов")]
  [InlineData(12, "файлов")]
  [InlineData(14, "файлов")]
  [InlineData(21, "файл")]
  [InlineData(22, "файла")]
  [InlineData(25, "файлов")]
  [InlineData(101, "файл")]
  [InlineData(111, "файлов")]
  public void PluralizeFiles_СклоняетПоЧислу(int count, string expected)
  {
    Assert.Equal(expected, MainViewModel.PluralizeFiles(count));
  }

  [Fact]
  public void FormatScanStatus_ФорматируетСчётчикИРазмер()
  {
    string s = MainViewModel.FormatScanStatus(new ScanProgress(2, 3 * 1024 * 1024));

    Assert.Equal("Сканирование: 2 файла, 3 МБ", s);
  }

  [Fact]
  public async Task Create_ФазаСканирования_ЗажигаетСчётчикИСбрасываетПосле()
  {
    byte[] a = Encoding.UTF8.GetBytes("первый");
    byte[] b = Encoding.UTF8.GetBytes("второй файл побольше для сжатия");
    string outPath = CreateTempPath();

    try
    {
      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          new ReportingSourceFilesPicker([new PickedFile("a.txt", a), new PickedFile("b.txt", b)]),
          new StubSaveFilePicker(outPath));

      bool sawScanning = false;
      string? lastScanStatus = null;
      vm.PropertyChanged += (_, e) =>
      {
        if (e.PropertyName == nameof(MainViewModel.IsScanning) && vm.IsScanning)
          sawScanning = true;
        if (e.PropertyName == nameof(MainViewModel.ScanStatus) && vm.ScanStatus is not null)
          lastScanStatus = vm.ScanStatus;
      };

      await vm.CreateCommand.ExecuteAsync();

      Assert.True(sawScanning);                       // счётчик зажигался по ходу
      Assert.NotNull(lastScanStatus);
      Assert.StartsWith("Сканирование: 2 файла,", lastScanStatus); // 2 файла прочитаны
      Assert.False(vm.IsScanning);                    // погас после завершения
      Assert.Null(vm.ScanStatus);
      Assert.True(File.Exists(outPath));              // архив всё же создан
    }
    finally
    {
      TryDeleteParent(outPath);
    }
  }

  private static string CreateTempPath()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiScanTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "out.7z");
  }

  private static void TryDeleteParent(string filePath)
  {
    try
    {
      string? dir = Path.GetDirectoryName(filePath);
      if (dir is not null && Directory.Exists(dir))
        Directory.Delete(dir, recursive: true);
    }
    catch
    {
      // best-effort
    }
  }
}
