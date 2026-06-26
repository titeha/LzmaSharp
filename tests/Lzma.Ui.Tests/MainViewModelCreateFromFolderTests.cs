using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

public sealed class MainViewModelCreateFromFolderTests
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

  private sealed class StubSourceFolderPicker(IReadOnlyList<PickedFile>? files) : ISourceFolderPicker
  {
    public Task<IReadOnlyList<PickedFile>?> PickFolderFilesAsync() => Task.FromResult(files);
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult(path);
  }

  private static string CreateTempPath()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiFolderTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return Path.Combine(dir, "out.7z");
  }

  [Fact]
  public async Task CreateFromFolder_ВложенныеИмена_ПопадаютВАрхив()
  {
    string outPath = CreateTempPath();

    IReadOnlyList<PickedFile> picked =
    [
        new PickedFile("docs/a.txt", Encoding.UTF8.GetBytes("первый")),
        new PickedFile("docs/sub/b.txt", Encoding.UTF8.GetBytes("второй, вложенный")),
    ];

    try
    {
      var vm = new MainViewModel(
          new StubArchivePicker(),
          new NullPasswordPrompt(),
          new StubFolderPicker(),
          new LzmaArchiveService(),
          sourceFilesPicker: null,
          new StubSaveFilePicker(outPath),
          new StubSourceFolderPicker(picked));

      Assert.True(vm.CanCreateFromFolder);

      await vm.CreateFromFolderCommand.ExecuteAsync();

      Assert.True(File.Exists(outPath));

      byte[] archive = File.ReadAllBytes(outPath);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, SevenZipDecodeOptions.Default, out SevenZipDecodedEntry[] entries));

      string[] names = entries.Select(e => e.Name.Replace('\\', '/')).ToArray();
      Assert.Contains("docs/a.txt", names);
      Assert.Contains("docs/sub/b.txt", names);
    }
    finally
    {
      TryDeleteParent(outPath);
    }
  }

  [Fact]
  public void CreateFromFolder_БезПапочногоПикера_КомандаНедоступна()
  {
    var vm = new MainViewModel(
        new StubArchivePicker(),
        new NullPasswordPrompt(),
        new StubFolderPicker());

    Assert.False(vm.CanCreateFromFolder);
    Assert.False(vm.CreateFromFolderCommand.CanExecute(null));
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
