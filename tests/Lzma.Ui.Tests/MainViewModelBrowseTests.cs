using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// Режим браузера файловой системы в главном окне (этап D, шаги 1–3).
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
  // OpenRead("C:\a.7z") отдаёт переданные байты архива (для теста открытия из браузера).
  private sealed class FakeBrowser(byte[]? archiveBytes = null) : IFileSystemBrowser
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

    public System.IO.Stream OpenRead(string fullPath) => fullPath == "C:\\a.7z" && archiveBytes is not null
        ? new System.IO.MemoryStream(archiveBytes, writable: false)
        : throw new System.IO.FileNotFoundException(fullPath);

    public IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths) => [];
  }

  private static MainViewModel CreateWithBrowser(byte[]? archiveBytes = null)
      => new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker(),
             new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null,
             sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new FakeBrowser(archiveBytes));

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

    // Одиночная навигация по файлу-архиву не открывает его (открытие — двойной клик/ActivateItemAsync).
    vm.NavigateInto(Find(vm, "a.7z"));

    Assert.Equal("C:\\", vm.CurrentPath);
    Assert.Contains(vm.Items, i => i.Name == "docs");
  }

  // ---- D3a: открытие архива из браузера двойным кликом ----

  [Fact]
  public async Task ActivateItem_Архив_ОткрываетЕгоСодержимое()
  {
    // Реальный 7z с одним файлом внутри.
    byte[] content = System.Text.Encoding.UTF8.GetBytes("inside the archive opened from browser");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    vm.NavigateInto(Find(vm, "C:\\"));

    await vm.ActivateItemAsync(Find(vm, "a.7z"));

    // Перешли в режим архива, видно его содержимое.
    Assert.True(vm.HasArchive);
    Assert.False(vm.IsFileSystemMode);
    Assert.Contains(vm.Items, i => i.Name == "inner.txt");
    Assert.Contains("a.7z", vm.Title);
  }

  [Fact]
  public async Task ActivateItem_Папка_ЗаходитВнутрь()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));

    await vm.ActivateItemAsync(Find(vm, "docs"));

    Assert.Equal("C:\\docs", vm.CurrentPath);
    Assert.Contains(vm.Items, i => i.Name == "readme.txt");
  }

  [Fact]
  public async Task ВыходИзАрхива_ВверхСКорня_ВозвращаетВБраузер()
  {
    byte[] content = System.Text.Encoding.UTF8.GetBytes("archive content for exit test");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    vm.NavigateInto(Find(vm, "C:\\"));            // C:\ (папка docs + a.7z)
    await vm.ActivateItemAsync(Find(vm, "a.7z")); // открыли архив

    Assert.True(vm.HasArchive);
    Assert.True(vm.CanGoUp);                       // «Вверх» доступен на корне архива (есть браузер)

    vm.NavigateUp();                               // выход из архива → браузер ФС

    Assert.False(vm.HasArchive);
    Assert.True(vm.IsFileSystemMode);
    Assert.Equal("C:\\", vm.CurrentPath);          // вернулись в папку, где лежал архив
    Assert.Contains(vm.Items, i => i.Name == "docs");
  }

  // ---- D2: мультивыбор ----

  [Fact]
  public void Выбор_ГалочкиОбновляютСчётчикИПути()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));

    Assert.Equal(0, vm.SelectedCount);
    Assert.False(vm.HasSelection);

    Find(vm, "docs").IsSelected = true;
    Find(vm, "a.7z").IsSelected = true;

    Assert.Equal(2, vm.SelectedCount);
    Assert.True(vm.HasSelection);
    Assert.Equal(["C:\\docs", "C:\\a.7z"], vm.SelectedPaths);

    Find(vm, "docs").IsSelected = false;
    Assert.Equal(1, vm.SelectedCount);
    Assert.Equal(["C:\\a.7z"], vm.SelectedPaths);
  }

  [Fact]
  public void Выбор_СбрасываетсяПриНавигации()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));
    Find(vm, "docs").IsSelected = true;
    Assert.Equal(1, vm.SelectedCount);

    vm.NavigateInto(Find(vm, "docs")); // переход в папку пересобирает список

    Assert.Equal(0, vm.SelectedCount);
    Assert.False(vm.HasSelection);
    Assert.Empty(vm.SelectedPaths);
  }

  [Fact]
  public void Выбор_ОтпискаПослеОчистки_НеСчитаетСтарыеЭлементы()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));
    ArchiveItem stale = Find(vm, "docs");

    vm.NavigateUp(); // список пересобран, stale больше не в Items

    // Изменение галочки на «оторванном» элементе не должно влиять на счётчик.
    stale.IsSelected = true;
    Assert.Equal(0, vm.SelectedCount);
  }
}
