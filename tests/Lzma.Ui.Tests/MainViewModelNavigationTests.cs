using System.Linq;

using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelNavigationTests
{
  private sealed class NullPicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class NullFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  private static MainViewModel CreateOpened(params SevenZipDecodedEntry[] entries)
  {
    var vm = new MainViewModel(new NullPicker(), new NullPasswordPrompt(), new NullFolderPicker());
    vm.ApplyResult("test.7z", SevenZipArchiveDecodeResult.Ok, entries);
    return vm;
  }

  // Архив без явных записей-каталогов — папки выводятся из путей файлов.
  private static MainViewModel CreateNestedArchive()
      => CreateOpened(
          new SevenZipDecodedEntry("docs/readme.txt", [1, 2, 3], false),
          new SevenZipDecodedEntry("docs/sub/deep.txt", [4, 5], false),
          new SevenZipDecodedEntry("root.txt", [7], false));

  private static ArchiveItem Item(MainViewModel vm, string name)
      => vm.Items.Single(i => i.Name == name);

  [Fact]
  public void Открытие_ПоказываетКореньИПапкиИзПутей()
  {
    MainViewModel vm = CreateNestedArchive();

    Assert.Equal(string.Empty, vm.CurrentPath);
    Assert.False(vm.CanGoUp);

    // В корне: папка docs (выведена из путей) и файл root.txt; папки первыми.
    Assert.Equal(2, vm.Items.Count);
    Assert.True(vm.Items[0].IsDirectory);
    Assert.Equal("docs", vm.Items[0].Name);
    Assert.Equal("root.txt", vm.Items[1].Name);
  }

  [Fact]
  public void ВходВПапку_ПоказываетЕёСодержимоеИПуть()
  {
    MainViewModel vm = CreateNestedArchive();

    vm.NavigateInto(Item(vm, "docs"));

    Assert.Equal("docs", vm.CurrentPath);
    Assert.True(vm.CanGoUp);
    Assert.True(vm.NavigateUpCommand.CanExecute(null));

    // Внутри docs: папка sub и файл readme.txt.
    Assert.Equal(2, vm.Items.Count);
    Assert.Equal("sub", vm.Items[0].Name);
    Assert.True(vm.Items[0].IsDirectory);
    Assert.Equal("readme.txt", vm.Items[1].Name);
    Assert.Equal(3, Item(vm, "readme.txt").Size);
  }

  [Fact]
  public void ВходВоВложеннуюПапку_СтроитПолныйПуть()
  {
    MainViewModel vm = CreateNestedArchive();

    vm.NavigateInto(Item(vm, "docs"));
    vm.NavigateInto(Item(vm, "sub"));

    Assert.Equal("docs/sub", vm.CurrentPath);
    ArchiveItem only = Assert.Single(vm.Items);
    Assert.Equal("deep.txt", only.Name);
  }

  [Fact]
  public void Вверх_ВозвращаетНаУровеньВыше()
  {
    MainViewModel vm = CreateNestedArchive();
    vm.NavigateInto(Item(vm, "docs"));
    vm.NavigateInto(Item(vm, "sub"));

    vm.NavigateUp();
    Assert.Equal("docs", vm.CurrentPath);
    Assert.True(vm.CanGoUp);

    vm.NavigateUp();
    Assert.Equal(string.Empty, vm.CurrentPath);
    Assert.False(vm.CanGoUp);
    Assert.False(vm.NavigateUpCommand.CanExecute(null));
  }

  [Fact]
  public void ВходВФайл_НичегоНеМеняет()
  {
    MainViewModel vm = CreateNestedArchive();

    vm.NavigateInto(Item(vm, "root.txt"));

    Assert.Equal(string.Empty, vm.CurrentPath);
    Assert.False(vm.CanGoUp);
    Assert.Equal(2, vm.Items.Count);
  }

  [Fact]
  public void ОткрытиеНовогоАрхива_СбрасываетНавигациюВКорень()
  {
    MainViewModel vm = CreateNestedArchive();
    vm.NavigateInto(Item(vm, "docs"));
    Assert.Equal("docs", vm.CurrentPath);

    vm.ApplyResult("flat.7z", SevenZipArchiveDecodeResult.Ok,
        [new SevenZipDecodedEntry("only.txt", [1], false)]);

    Assert.Equal(string.Empty, vm.CurrentPath);
    Assert.False(vm.CanGoUp);
    ArchiveItem only = Assert.Single(vm.Items);
    Assert.Equal("only.txt", only.Name);
  }
}
