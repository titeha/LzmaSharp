using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// D3b: создание архива из отмеченного в браузере (файлы + папки), потоково на диск.
public sealed class MainViewModelCreateFromSelectionTests
{
  private sealed class StubArchivePicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class CancellingPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class CancellingFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  private sealed class StubSaveFilePicker(string path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult<string?>(path);
  }

  // Браузер поверх реальной ФС, но с корнем = заданная временная папка (чтобы дойти навигацией).
  private sealed class RootedBrowser(string root) : IFileSystemBrowser
  {
    private readonly DesktopFileSystemBrowser _real = new();

    public IReadOnlyList<FileSystemEntry> ListRoots() => [new(root, root, IsDirectory: true, Size: 0)];
    public IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath) => _real.ListDirectory(fullPath);
    public string? GetParent(string fullPath) => fullPath == root ? null : _real.GetParent(fullPath);
    public System.IO.Stream OpenRead(string fullPath) => _real.OpenRead(fullPath);
    public IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths)
        => _real.EnumerateForArchive(paths);
  }

  [Fact]
  public async Task CreateFromSelection_ФайлИПапка_СоздаётАрхивСПравильнымиИменами()
  {
    string work = Path.Combine(Path.GetTempPath(), "lzs-sel-" + Guid.NewGuid().ToString("N"));
    string src = Path.Combine(work, "src");
    Directory.CreateDirectory(Path.Combine(src, "photos", "sub"));
    File.WriteAllBytes(Path.Combine(src, "report.txt"), Encoding.UTF8.GetBytes("top-level file"));
    File.WriteAllBytes(Path.Combine(src, "photos", "a.txt"), Encoding.UTF8.GetBytes("in folder"));
    File.WriteAllBytes(Path.Combine(src, "photos", "sub", "b.txt"), Encoding.UTF8.GetBytes("nested"));

    string archivePath = Path.Combine(work, "out.7z");

    try
    {
      MainViewModel vm = new(
          new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker(),
          new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: new StubSaveFilePicker(archivePath),
          sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new RootedBrowser(src));

      // На старте показан список корней (единственный — src); заходим внутрь.
      vm.NavigateInto(vm.Items[0]);

      // Отмечаем файл report.txt и папку photos.
      Find(vm, "report.txt").IsSelected = true;
      Find(vm, "photos").IsSelected = true;
      Assert.Equal(2, vm.SelectedCount);
      Assert.True(vm.CanCreateFromSelection);

      await vm.CreateFromSelectionCommand.ExecuteAsync();

      Assert.True(File.Exists(archivePath));

      // Проверяем содержимое: имена сохранили структуру (папка + относительные пути).
      SevenZipArchiveDecodeResult decode = SevenZipArchiveDecoder.DecodeToEntries(
          File.ReadAllBytes(archivePath), out SevenZipDecodedEntry[] entries);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, decode);

      string[] names = [.. entries.Where(e => !e.IsDirectory).Select(e => e.Name.Replace('\\', '/'))];
      Assert.Contains("report.txt", names);
      Assert.Contains("photos/a.txt", names);
      Assert.Contains("photos/sub/b.txt", names);
    }
    finally
    {
      if (Directory.Exists(work))
        Directory.Delete(work, recursive: true);
    }
  }

  private static ArchiveItem Find(MainViewModel vm, string name)
      => vm.Items.Single(i => i.Name == name);
}
