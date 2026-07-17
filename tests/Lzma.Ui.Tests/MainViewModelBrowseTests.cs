using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// Режим браузера файловой системы в главном окне (этап D, шаг 1: навигация по ФС).
public sealed class MainViewModelBrowseTests
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

  // Фейковая ФС: корень C:\ с папкой docs и архивом a.7z; docs содержит readme.txt.
  private sealed class FakeBrowser : IFileSystemBrowser
  {
    public IReadOnlyList<FileSystemEntry> ListRoots() =>
        [new("C:\\", "C:\\", IsDirectory: true, Size: 0)];

    public IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath) => fullPath switch
    {
      "C:\\" =>
      [
        new("docs", "C:\\docs", IsDirectory: true, Size: 0),
        new("a.7z", "C:\\a.7z", IsDirectory: false, Size: 1234),
      ],
      "C:\\docs" =>
      [
        new("readme.txt", "C:\\docs\\readme.txt", IsDirectory: false, Size: 42),
      ],
      _ => [],
    };

    public string? GetParent(string fullPath) => fullPath switch
    {
      "C:\\docs" => "C:\\",
      "C:\\" => null,
      _ => null,
    };
  }

  private static MainViewModel CreateWithBrowser()
      => new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker(),
             new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null,
             sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new FakeBrowser());

  private static ArchiveItem Find(MainViewModel vm, string name)
      => vm.Items.Single(i => i.Name == name);

  [Fact]
  public void СБраузером_НаСтарте_ПоказываетКорниВРежимеФС()
  {
    MainViewModel vm = CreateWithBrowser();

    Assert.True(vm.IsFileSystemMode);
    Assert.True(vm.HasContent);
    Assert.False(vm.HasArchive);
    Assert.Equal("Этот компьютер", vm.CurrentPath);
    Assert.False(vm.CanGoUp);

    Assert.Single(vm.Items);
    Assert.Equal("C:\\", vm.Items[0].Name);
    Assert.True(vm.Items[0].IsDirectory);
  }

  [Fact]
  public void БезБраузера_РежимФСВыключен_ПустоеСостояние()
  {
    MainViewModel vm = new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker());

    Assert.False(vm.IsFileSystemMode);
    Assert.False(vm.HasContent);
    Assert.Empty(vm.Items);
  }

  [Fact]
  public void NavigateInto_Диск_ПоказываетСодержимое()
  {
    MainViewModel vm = CreateWithBrowser();

    vm.NavigateInto(Find(vm, "C:\\"));

    Assert.Equal("C:\\", vm.CurrentPath);
    Assert.True(vm.CanGoUp);

    // Папки идут первыми; архив помечен значком/типом.
    Assert.Equal("docs", vm.Items[0].Name);
    Assert.True(vm.Items[0].IsDirectory);

    ArchiveItem archive = Find(vm, "a.7z");
    Assert.False(archive.IsDirectory);
    Assert.True(archive.IsArchiveFile);
    Assert.Equal("архив", archive.Kind);
    Assert.Equal(1234, archive.Size);
  }

  [Fact]
  public void NavigateInto_Папка_ЗатемВверх_ВозвращаетНазад()
  {
    MainViewModel vm = CreateWithBrowser();

    vm.NavigateInto(Find(vm, "C:\\"));      // C:\
    vm.NavigateInto(Find(vm, "docs"));       // C:\docs

    Assert.Equal("C:\\docs", vm.CurrentPath);
    Assert.Single(vm.Items);
    Assert.Equal("readme.txt", vm.Items[0].Name);
    Assert.Equal(42, vm.Items[0].Size);

    vm.NavigateUp();                          // → C:\
    Assert.Equal("C:\\", vm.CurrentPath);
    Assert.True(vm.CanGoUp);
    Assert.Contains(vm.Items, i => i.Name == "docs");

    vm.NavigateUp();                          // → корни
    Assert.Equal("Этот компьютер", vm.CurrentPath);
    Assert.False(vm.CanGoUp);
    Assert.Single(vm.Items);
  }

  [Fact]
  public void NavigateInto_Файл_НичегоНеМеняет()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));

    // Двойной клик по файлу-архиву пока не открывает его (следующий шаг), список не меняется.
    vm.NavigateInto(Find(vm, "a.7z"));

    Assert.Equal("C:\\", vm.CurrentPath);
    Assert.Contains(vm.Items, i => i.Name == "docs");
  }
}
