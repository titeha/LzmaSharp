using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// Браузер файловой системы в главном окне: раскрываемое ДЕРЕВО от дисков (этап D + UI-волна S4).
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

    public string? ResolveDirectory(string path)
    {
      string p = path.Trim().TrimEnd('\\', '/');
      return p switch { "C:" => "C:\\", "C:\\docs" => "C:\\docs", _ => null };
    }
  }

  private static MainViewModel CreateWithBrowser(byte[]? archiveBytes = null)
      => new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker(),
             new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null,
             sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new FakeBrowser(archiveBytes));

  // Узел дерева ФС верхнего уровня (диск).
  private static TreeNodeItem Root(MainViewModel vm, string name) => vm.FileSystemTree.Single(n => n.Name == name);

  // Раскрывает узел (ленивая догрузка) и возвращает ребёнка по имени.
  private static TreeNodeItem Child(TreeNodeItem node, string name)
  {
    node.IsExpanded = true;
    return node.Children.Single(c => c.Name == name);
  }

  // Элемент плоского списка (режим архива).
  private static ArchiveItem Item(MainViewModel vm, string name) => vm.Items.Single(i => i.Name == name);

  // ---- Дерево ФС ----

  [Fact]
  public void СБраузером_НаСтарте_ПоказываетДеревоДисков()
  {
    MainViewModel vm = CreateWithBrowser();

    Assert.True(vm.IsFileSystemMode);
    Assert.True(vm.HasContent);
    Assert.False(vm.HasArchive);
    Assert.False(vm.CanGoUp);

    Assert.Single(vm.FileSystemTree);
    Assert.Equal("C:\\", vm.FileSystemTree[0].Name);
    Assert.True(vm.FileSystemTree[0].IsDirectory);
    Assert.Empty(vm.Items); // в ФС плоский список не используется
  }

  // Браузер со специальными папками (быстрый доступ) + один диск.
  private sealed class SpecialFoldersBrowser : IFileSystemBrowser
  {
    public IReadOnlyList<FileSystemEntry> ListRoots() => [new("C:\\", "C:\\", IsDirectory: true, Size: 0)];
    public IReadOnlyList<FileSystemEntry> ListSpecialFolders() =>
        [new("Загрузки", "D:\\dl", IsDirectory: true, Size: 0), new("Документы", "D:\\doc", IsDirectory: true, Size: 0)];
    public IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath) => [];
    public string? GetParent(string fullPath) => null;
    public System.IO.Stream OpenRead(string fullPath) => throw new System.NotSupportedException();
    public IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths) => [];
  }

  [Fact]
  public void Дерево_СпециальныеПапки_ПередДисками()
  {
    MainViewModel vm = new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker(),
        new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null,
        sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new SpecialFoldersBrowser());

    Assert.Equal(["Загрузки", "Документы", "C:\\"], vm.FileSystemTree.Select(n => n.Name));
    Assert.True(vm.FileSystemTree[0].IsDirectory);
  }

  [Fact]
  public void БезБраузера_РежимФСВыключен_ПустоеСостояние()
  {
    MainViewModel vm = new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker());

    Assert.False(vm.IsFileSystemMode);
    Assert.False(vm.HasContent);
    Assert.Empty(vm.FileSystemTree);
  }

  [Fact]
  public void Дерево_РаскрытиеДиска_ПоказываетСодержимое()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem disk = Root(vm, "C:\\");

    disk.IsExpanded = true; // ленивая догрузка

    Assert.Equal(2, disk.Children.Count);
    Assert.Equal("docs", disk.Children[0].Name); // папки первыми
    Assert.True(disk.Children[0].IsDirectory);

    TreeNodeItem archive = disk.Children.Single(c => c.Name == "a.7z");
    Assert.True(archive.IsArchiveFile);
    Assert.Equal("архив", archive.Kind);
    Assert.Equal(1234, archive.Size);
  }

  [Fact]
  public void Дерево_РаскрытиеПапки_ЛенивоГрузит()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem docs = Child(Root(vm, "C:\\"), "docs");

    Assert.False(docs.IsLoaded);
    docs.IsExpanded = true;

    Assert.True(docs.IsLoaded);
    Assert.Single(docs.Children);
    Assert.Equal("readme.txt", docs.Children[0].Name);
    Assert.Equal(42, docs.Children[0].Size);
  }

  // ---- Выбор по дереву ----

  [Fact]
  public void Выбор_ГалочкиОбновляютСчётчикИПути()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem disk = Root(vm, "C:\\");
    disk.IsExpanded = true;

    Assert.Equal(0, vm.SelectedCount);
    Assert.False(vm.HasSelection);

    Child(disk, "docs").IsSelected = true;
    disk.Children.Single(c => c.Name == "a.7z").IsSelected = true;

    Assert.Equal(2, vm.SelectedCount);
    Assert.True(vm.HasSelection);
    Assert.Equal(["C:\\docs", "C:\\a.7z"], vm.SelectedPaths);

    Child(disk, "docs").IsSelected = false;
    Assert.Equal(1, vm.SelectedCount);
    Assert.Equal(["C:\\a.7z"], vm.SelectedPaths);
  }

  [Fact]
  public void Выбор_ОтмеченнаяПапка_ПокрываетПоддерево()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem docs = Child(Root(vm, "C:\\"), "docs");
    docs.IsExpanded = true; // догрузили readme.txt

    docs.IsSelected = true; // отмечена папка

    // В путях — только папка (её поддерево покрыто); в readme.txt не спускаемся.
    Assert.Equal(["C:\\docs"], vm.SelectedPaths);
  }

  [Fact]
  public void Выбор_ВложенныйУзел_Считается()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem readme = Child(Child(Root(vm, "C:\\"), "docs"), "readme.txt");

    readme.IsSelected = true;

    Assert.Equal(1, vm.SelectedCount);
    Assert.Equal(["C:\\docs\\readme.txt"], vm.SelectedPaths);
  }

  // ---- Адресная строка → раскрытие дерева до пути ----

  [Fact]
  public void АдреснаяСтрока_ВводПапки_РаскрываетДеревоИВыделяет()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.BeginEditPath();
    Assert.True(vm.IsEditingPath);

    vm.EditablePath = "C:\\docs";
    vm.CommitPath();

    Assert.False(vm.IsEditingPath);
    Assert.True(Root(vm, "C:\\").IsExpanded);          // диск раскрыт
    Assert.NotNull(vm.SelectedTreeNode);
    Assert.Equal("docs", vm.SelectedTreeNode!.Name);   // выделена нужная папка
  }

  [Fact]
  public void АдреснаяСтрока_НеверныйПуть_Статус_ОстаётсяВвод()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.BeginEditPath();
    vm.EditablePath = "C:\\nope";
    vm.CommitPath();

    Assert.True(vm.IsEditingPath); // поле остаётся для правки
    Assert.Contains("не папка", vm.StatusMessage);
  }

  [Fact]
  public void АдреснаяСтрока_Отмена_ГаситВвод()
  {
    MainViewModel vm = CreateWithBrowser();
    vm.BeginEditPath();
    vm.EditablePath = "C:\\docs";
    vm.CancelEditPath();

    Assert.False(vm.IsEditingPath);
    Assert.Null(vm.SelectedTreeNode); // отмена не переходит
  }

  [Fact]
  public void АдреснаяСтрока_РедактированиеТолькоВФС()
  {
    MainViewModel vm = CreateWithBrowser();
    Assert.True(vm.CanEditPath); // режим ФС

    MainViewModel noBrowser = new(new StubArchivePicker(), new CancellingPasswordPrompt(), new CancellingFolderPicker());
    Assert.False(noBrowser.CanEditPath); // без браузера — нет режима ФС
  }

  // ---- Открытие архива из дерева двойным кликом ----

  [Fact]
  public async Task ActivateTreeNode_Архив_ОткрываетЕгоСодержимое()
  {
    byte[] content = System.Text.Encoding.UTF8.GetBytes("inside the archive opened from tree");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    TreeNodeItem node = Child(Root(vm, "C:\\"), "a.7z");

    await vm.ActivateTreeNodeAsync(node);

    Assert.True(vm.HasArchive);
    Assert.False(vm.IsFileSystemMode);
    Assert.Contains(vm.Items, i => i.Name == "inner.txt");
    Assert.Contains("a.7z", vm.Title);
  }

  [Fact]
  public async Task ActivateTreeNode_Папка_НеОткрывает()
  {
    MainViewModel vm = CreateWithBrowser();
    TreeNodeItem docs = Child(Root(vm, "C:\\"), "docs");

    await vm.ActivateTreeNodeAsync(docs);

    Assert.False(vm.HasArchive); // папку двойной клик не открывает (раскрытие — треугольником)
  }

  [Fact]
  public async Task ВыходИзАрхива_ВверхСКорня_ВозвращаетВДеревоФС()
  {
    byte[] content = System.Text.Encoding.UTF8.GetBytes("archive content for exit test");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    await vm.ActivateTreeNodeAsync(Child(Root(vm, "C:\\"), "a.7z"));

    Assert.True(vm.HasArchive);
    Assert.True(vm.CanGoUp); // «Вверх» доступен на корне архива

    vm.NavigateUp(); // выход из архива → дерево ФС

    Assert.False(vm.HasArchive);
    Assert.True(vm.IsFileSystemMode);
    Assert.Single(vm.FileSystemTree);
    Assert.Equal("C:\\", vm.FileSystemTree[0].Name);
  }

  // ---- Архивные крошки (режим архива остаётся плоским) ----

  [Fact]
  public async Task Крошки_ВАрхиве_КореньИмяАрхива_ГлубинаНавигации()
  {
    byte[] content = System.Text.Encoding.UTF8.GetBytes("nested archive entry");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("folder/inner.txt", content)], out byte[] archive));

    MainViewModel vm = CreateWithBrowser(archive);
    await vm.ActivateTreeNodeAsync(Child(Root(vm, "C:\\"), "a.7z")); // открыли архив, корень

    Assert.Single(vm.Breadcrumbs);
    Assert.Contains("a.7z", vm.Breadcrumbs[0].Name);
    Assert.True(vm.Breadcrumbs[0].IsCurrent);
    Assert.Equal(0, vm.Breadcrumbs[0].Depth);

    vm.NavigateInto(Item(vm, "folder")); // вошли в folder (архив — плоская навигация)

    Assert.Equal(2, vm.Breadcrumbs.Count);
    Assert.Equal("folder", vm.Breadcrumbs[1].Name);
    Assert.True(vm.Breadcrumbs[1].IsCurrent);

    vm.NavigateToCrumb(vm.Breadcrumbs[0]); // клик по корню архива
    Assert.Contains(vm.Items, i => i.Name == "folder");
    Assert.Single(vm.Breadcrumbs);
  }
}
