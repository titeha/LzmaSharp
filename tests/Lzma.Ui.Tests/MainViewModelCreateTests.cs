using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelCreateTests
{
  private sealed class StubArchivePicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
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

  private sealed class StubSourceFilesPicker(IReadOnlyList<PickedFile>? files) : ISourceFilesPicker
  {
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(System.IProgress<ScanProgress>? progress = null)
        => Task.FromResult(files);
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public string? RequestedSuggestion { get; private set; }

    public Task<string?> PickSavePathAsync(string suggestedFileName)
    {
      RequestedSuggestion = suggestedFileName;
      return Task.FromResult(path);
    }
  }

  private static MainViewModel BuildViewModel(
      IReadOnlyList<PickedFile>? files,
      string? savePath,
      out StubSaveFilePicker savePicker)
  {
    savePicker = new StubSaveFilePicker(savePath);

    return new MainViewModel(
        new StubArchivePicker(null),
        new NullPasswordPrompt(),
        new StubFolderPicker(),
        new LzmaArchiveService(),
        new StubSourceFilesPicker(files),
        savePicker);
  }

  private static string CreateTempPath()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiCreateTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "out.7z");
  }

  [Fact]
  public async Task Create_ВыбраныФайлы_ПишетАрхивКоторыйОткрываетсяОбратно()
  {
    byte[] a = Encoding.UTF8.GetBytes("первый");
    byte[] b = Encoding.UTF8.GetBytes("второй файл побольше для сжатия LZMA2");

    string outPath = CreateTempPath();

    try
    {
      MainViewModel vm = BuildViewModel(
          [new PickedFile("a.txt", a), new PickedFile("b.txt", b)],
          outPath,
          out _);

      vm.SelectedCompressionMethod = SevenZipWriterCompressionMethod.Lzma2;

      await vm.CreateCommand.ExecuteAsync();

      Assert.False(vm.IsBusy);
      Assert.True(File.Exists(outPath));
      Assert.Contains(outPath, vm.StatusMessage);

      // Архив должен открываться нашим же декодером с тем же содержимым.
      byte[] archiveBytes = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archiveBytes, SevenZipDecodeOptions.Default, out SevenZipDecodedEntry[] entries));
      Assert.Equal(2, entries.Length);
    }
    finally
    {
      TryDeleteParent(outPath);
    }
  }

  [Fact]
  public async Task Create_ОтменаВыбораФайлов_НичегоНеПишет()
  {
    MainViewModel vm = BuildViewModel(files: null, savePath: null, out StubSaveFilePicker savePicker);

    await vm.CreateCommand.ExecuteAsync();

    Assert.False(vm.IsBusy);
    Assert.Null(vm.StatusMessage);
    Assert.Null(savePicker.RequestedSuggestion); // до выбора пути дело не дошло
  }

  [Fact]
  public async Task Create_ОтменаВыбораПути_НичегоНеПишет()
  {
    byte[] a = Encoding.UTF8.GetBytes("x");

    MainViewModel vm = BuildViewModel(
        [new PickedFile("a.txt", a)],
        savePath: null, // путь не выбран
        out StubSaveFilePicker savePicker);

    await vm.CreateCommand.ExecuteAsync();

    Assert.False(vm.IsBusy);
    Assert.Equal("archive.7z", savePicker.RequestedSuggestion); // путь спрашивали
    Assert.Null(vm.StatusMessage);
  }

  [Fact]
  public void Create_БезПикеров_КомандаНедоступна()
  {
    var vm = new MainViewModel(
        new StubArchivePicker(null),
        new NullPasswordPrompt(),
        new StubFolderPicker());

    Assert.False(vm.CanCreate);
    Assert.False(vm.CreateCommand.CanExecute(null));
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
