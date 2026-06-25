using System.IO;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelExtractTests
{
  private sealed class StubPicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker(string? folder) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
  }

  private static string CreateTempDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  [Fact]
  public async Task ExtractAll_ПростойАрхив_ПишетФайлВПапку()
  {
    byte[] content = Encoding.UTF8.GetBytes("extract me to disk");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("note.txt", content)], out byte[] archive));

    string dest = CreateTempDir();

    try
    {
      var vm = new MainViewModel(
          new StubPicker(new PickedArchive("a.7z", archive)),
          new NullPasswordPrompt(),
          new StubFolderPicker(dest));

      await vm.OpenCommand.ExecuteAsync();
      Assert.True(vm.HasArchive);

      await vm.ExtractAllCommand.ExecuteAsync();

      string filePath = Path.Combine(dest, "note.txt");
      Assert.True(File.Exists(filePath));
      Assert.Equal(content, File.ReadAllBytes(filePath));
      Assert.False(vm.IsBusy);
      Assert.Contains(dest, vm.StatusMessage);
    }
    finally
    {
      TryDelete(dest);
    }
  }

  [Fact]
  public async Task ExtractAll_ЗашифрованныйГостАрхив_ИзвлекаетСПаролем()
  {
    byte[] content = Encoding.UTF8.GetBytes("secret payload for extraction");

    using SevenZipPassword password = SevenZipPassword.FromString("p@ss");
    var options = new SevenZipGostEncryptionOptions
    {
      Cipher = SevenZipGostCipher.Kuznyechik,
      Password = password,
      NumCyclesPower = 4,
    };
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildGostEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)], options, out byte[] archive));

    string dest = CreateTempDir();

    try
    {
      var vm = new MainViewModel(
          new StubPicker(new PickedArchive("enc.7z", archive)),
          new SinglePasswordPrompt("p@ss"),
          new StubFolderPicker(dest));

      await vm.OpenCommand.ExecuteAsync();
      Assert.True(vm.HasArchive);

      await vm.ExtractAllCommand.ExecuteAsync();

      string filePath = Path.Combine(dest, "secret.txt");
      Assert.True(File.Exists(filePath));
      Assert.Equal(content, File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDelete(dest);
    }
  }

  [Fact]
  public async Task ExtractAll_ОтменаВыбораПапки_НичегоНеПишет()
  {
    byte[] content = Encoding.UTF8.GetBytes("x");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("note.txt", content)], out byte[] archive));

    var vm = new MainViewModel(
        new StubPicker(new PickedArchive("a.7z", archive)),
        new NullPasswordPrompt(),
        new StubFolderPicker(null)); // отмена выбора папки

    await vm.OpenCommand.ExecuteAsync();
    await vm.ExtractAllCommand.ExecuteAsync();

    Assert.False(vm.IsBusy);
  }

  [Fact]
  public void ExtractAll_БезОткрытогоАрхива_КомандаНедоступна()
  {
    var vm = new MainViewModel(
        new StubPicker(null),
        new NullPasswordPrompt(),
        new StubFolderPicker(null));

    Assert.False(vm.ExtractAllCommand.CanExecute(null));
  }

  private sealed class SinglePasswordPrompt(string password) : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(password);
  }

  private static void TryDelete(string dir)
  {
    try
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, recursive: true);
    }
    catch
    {
      // best-effort
    }
  }
}
