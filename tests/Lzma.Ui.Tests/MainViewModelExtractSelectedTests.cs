using System.IO;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// «Извлечь выбранное»: из открытого архива на диск пишутся только отмеченные записи (файл — точно,
/// папка — поддерево). Через реальный сервис (7z in-memory и ZIP in-memory).
/// </summary>
public sealed class MainViewModelExtractSelectedTests
{
  private sealed class StubPicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed) => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker(string? folder) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
  }

  private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

  private static string CreateTempDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiSel", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static void TryDelete(string dir)
  {
    try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
  }

  private static MainViewModel OpenVm(PickedArchive picked, string dest)
  {
    var vm = new MainViewModel(new StubPicker(picked), new NullPasswordPrompt(), new StubFolderPicker(dest));
    vm.OpenCommand.ExecuteAsync().GetAwaiter().GetResult();
    Assert.True(vm.HasArchive);
    return vm;
  }

  private static byte[] Build7z(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] bytes));
    return bytes;
  }

  [Fact]
  public async Task ExtractSelected_7z_ТолькоОтмеченныйФайл()
  {
    byte[] a = Text("alpha"), x = Text("doc x"), y = Text("doc y");
    byte[] archive = Build7z(("a.txt", a), ("docs/x.txt", x), ("docs/y.txt", y));
    string dest = CreateTempDir();
    try
    {
      MainViewModel vm = OpenVm(new PickedArchive("t.7z", archive), dest);
      vm.Items.First(i => i.Name == "a.txt").IsSelected = true;
      Assert.True(vm.CanExtractSelected);

      await vm.ExtractSelectedCommand.ExecuteAsync();

      Assert.Equal(a, File.ReadAllBytes(Path.Combine(dest, "a.txt")));
      Assert.False(Directory.Exists(Path.Combine(dest, "docs")));
    }
    finally { TryDelete(dest); }
  }

  [Fact]
  public async Task ExtractSelected_7z_ВыборПапки_ИзвлекаетПоддерево()
  {
    byte[] a = Text("alpha"), x = Text("doc x"), y = Text("doc y");
    byte[] archive = Build7z(("a.txt", a), ("docs/x.txt", x), ("docs/y.txt", y));
    string dest = CreateTempDir();
    try
    {
      MainViewModel vm = OpenVm(new PickedArchive("t.7z", archive), dest);
      vm.Items.First(i => i.Name == "docs" && i.IsDirectory).IsSelected = true;

      await vm.ExtractSelectedCommand.ExecuteAsync();

      Assert.Equal(x, File.ReadAllBytes(Path.Combine(dest, "docs", "x.txt")));
      Assert.Equal(y, File.ReadAllBytes(Path.Combine(dest, "docs", "y.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }
    finally { TryDelete(dest); }
  }

  [Fact]
  public async Task ExtractSelected_Zip_ТолькоОтмеченные()
  {
    byte[] keep = Text("keep this"), drop = Text("drop this");
    ZipWriter.Build([new ZipWriterEntry("keep.txt", keep), new ZipWriterEntry("drop.txt", drop)], out byte[] archive);
    string dest = CreateTempDir();
    try
    {
      MainViewModel vm = OpenVm(new PickedArchive("t.zip", archive), dest);
      vm.Items.First(i => i.Name == "keep.txt").IsSelected = true;

      await vm.ExtractSelectedCommand.ExecuteAsync();

      Assert.Equal(keep, File.ReadAllBytes(Path.Combine(dest, "keep.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "drop.txt")));
    }
    finally { TryDelete(dest); }
  }

  [Fact]
  public async Task ExtractSelected_БезВыбора_Недоступна()
  {
    byte[] archive = Build7z(("a.txt", Text("x")));
    string dest = CreateTempDir();
    try
    {
      MainViewModel vm = OpenVm(new PickedArchive("t.7z", archive), dest);
      Assert.False(vm.CanExtractSelected);
      Assert.False(vm.ExtractSelectedCommand.CanExecute(null));
    }
    finally { TryDelete(dest); }
  }
}
