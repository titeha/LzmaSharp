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

  // ---- D4: хлебные крошки пути ----

  private static PathCrumb Crumb(MainViewModel vm, string name)
      => vm.Breadcrumbs.Single(c => c.Name == name);

  [Fact]
  public void Крошки_НаКорнях_ОдинСегментЭтотКомпьютер()
  {
    MainViewModel vm = CreateWithBrowser();

    Assert.Single(vm.Breadcrumbs);
    Assert.Equal("Этот компьютер", vm.Breadcrumbs[0].Name);
    Assert.Null(vm.Breadcrumbs[0].FullPath);
    Assert.True(vm.Breadcrumbs[0].IsCurrent);
  }

  [Fact]
  public void Крошки_ВложеннаяПапка_СтроятПутьОтКорня()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));   // C:\
    vm.NavigateInto(Find(vm, "docs"));    // C:\docs

    Assert.Equal(["Этот компьютер", "C:", "docs"], vm.Breadcrumbs.Select(c => c.Name));
    Assert.Null(vm.Breadcrumbs[0].FullPath);
    Assert.Equal("C:\\", vm.Breadcrumbs[1].FullPath);
    Assert.Equal("C:\\docs", vm.Breadcrumbs[2].FullPath);

    // Текущий сегмент — только последний.
    Assert.False(vm.Breadcrumbs[0].IsCurrent);
    Assert.False(vm.Breadcrumbs[1].IsCurrent);
    Assert.True(vm.Breadcrumbs[2].IsCurrent);
  }

  [Fact]
  public void NavigateToCrumb_ПромежуточныйСегмент_ПереходитТуда()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));
    vm.NavigateInto(Find(vm, "docs"));    // C:\docs

    vm.NavigateToCrumb(Crumb(vm, "C:"));  // назад на диск

    Assert.Equal("C:\\", vm.CurrentPath);
    Assert.Contains(vm.Items, i => i.Name == "docs");
  }

  [Fact]
  public void NavigateToCrumb_КореньЭтотКомпьютер_ВозвращаетККорням()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));
    vm.NavigateInto(Find(vm, "docs"));

    vm.NavigateToCrumb(Crumb(vm, "Этот компьютер"));

    Assert.Equal("Этот компьютер", vm.CurrentPath);
    Assert.False(vm.CanGoUp);
    Assert.Single(vm.Items);
  }

  [Fact]
  public void NavigateToCrumb_ТекущийСегмент_НичегоНеМеняет()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.NavigateInto(Find(vm, "C:\\"));   // C:\ — текущая крошка «C:»

    vm.NavigateToCrumb(Crumb(vm, "C:")); // клик по текущей крошке — no-op

    Assert.Equal("C:\\", vm.CurrentPath);
  }

  [Fact]
  public async Task Крошки_ВАрхиве_КореньИмяАрхива_ГлубинаНавигации()
  {
    // Архив с вложенной папкой: folder/inner.txt.
    byte[] content = System.Text.Encoding.UTF8.GetBytes("nested archive entry");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("folder/inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    vm.NavigateInto(Find(vm, "C:\\"));
    await vm.ActivateItemAsync(Find(vm, "a.7z")); // открыли архив, корень

    // На корне архива крошка одна — имя архива.
    Assert.Single(vm.Breadcrumbs);
    Assert.Contains("a.7z", vm.Breadcrumbs[0].Name);
    Assert.True(vm.Breadcrumbs[0].IsCurrent);
    Assert.Equal(0, vm.Breadcrumbs[0].Depth);

    vm.NavigateInto(Find(vm, "folder"));           // вошли в folder

    Assert.Equal(2, vm.Breadcrumbs.Count);
    Assert.Equal("folder", vm.Breadcrumbs[1].Name);
    Assert.True(vm.Breadcrumbs[1].IsCurrent);
    Assert.Equal(1, vm.Breadcrumbs[1].Depth);

    vm.NavigateToCrumb(vm.Breadcrumbs[0]);          // клик по корню архива

    Assert.Contains(vm.Items, i => i.Name == "folder");
    Assert.Single(vm.Breadcrumbs);
  }

  // ---- D4: чистый построитель крошек ФС ----

  [Fact]
  public void BuildFileSystemCrumbs_Корни_Null_ОдинТекущийСегмент()
  {
    var crumbs = MainViewModel.BuildFileSystemCrumbs(null);

    Assert.Single(crumbs);
    Assert.Equal("Этот компьютер", crumbs[0].Name);
    Assert.Null(crumbs[0].FullPath);
    Assert.True(crumbs[0].IsCurrent);
  }

  [Fact]
  public void BuildFileSystemCrumbs_КореньДиска_ДваСегмента()
  {
    var crumbs = MainViewModel.BuildFileSystemCrumbs("C:\\");

    Assert.Equal(["Этот компьютер", "C:"], crumbs.Select(c => c.Name));
    Assert.Null(crumbs[0].FullPath);
    Assert.Equal("C:\\", crumbs[1].FullPath);
    Assert.True(crumbs[1].IsCurrent);
  }

  [Fact]
  public void BuildFileSystemCrumbs_ВложенныйПуть_НакапливаетПолныеПути()
  {
    var crumbs = MainViewModel.BuildFileSystemCrumbs("C:\\Users\\Артемий\\docs");

    Assert.Equal(["Этот компьютер", "C:", "Users", "Артемий", "docs"], crumbs.Select(c => c.Name));
    Assert.Equal("C:\\", crumbs[1].FullPath);
    Assert.Equal("C:\\Users", crumbs[2].FullPath);
    Assert.Equal("C:\\Users\\Артемий", crumbs[3].FullPath);
    Assert.Equal("C:\\Users\\Артемий\\docs", crumbs[4].FullPath);
    Assert.True(crumbs[^1].IsCurrent);
  }
}
