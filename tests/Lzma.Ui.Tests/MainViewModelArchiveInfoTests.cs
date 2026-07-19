using System;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

// Окно «Информация об архиве»: сводка (счётчики/размеры/коэффициент) по открытому архиву.
public sealed class MainViewModelArchiveInfoTests
{
  private sealed class StubPicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed) => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  // Браузер с корнем = заданная папка (реальная ФС), чтобы выбрать реальный файл-архив.
  private sealed class RootedBrowser(string root) : IFileSystemBrowser
  {
    private readonly DesktopFileSystemBrowser _real = new();
    public IReadOnlyList<FileSystemEntry> ListRoots() => [new(root, root, IsDirectory: true, Size: 0)];
    public IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath) => _real.ListDirectory(fullPath);
    public string? GetParent(string fullPath) => fullPath == root ? null : _real.GetParent(fullPath);
    public System.IO.Stream OpenRead(string fullPath) => _real.OpenRead(fullPath);
    public IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths) => _real.EnumerateForArchive(paths);
  }

  [Fact]
  public async Task BuildArchiveInfoAsync_ВыбранныйАрхив_ЧитаетЛистинг()
  {
    byte[] x = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("документ ", 400)));
    byte[] y = Encoding.UTF8.GetBytes("короткий");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("docs/x.txt", x), new SevenZipArchiveWriterEntry("y.txt", y)],
        SevenZipWriterCompressionMethod.Lzma2, out byte[] archiveBytes));

    string dir = Path.Combine(Path.GetTempPath(), "lzs-info-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "data.7z");
    await File.WriteAllBytesAsync(archivePath, archiveBytes);

    try
    {
      var vm = new MainViewModel(new StubPicker(null), new NullPasswordPrompt(), new StubFolderPicker(),
          new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null,
          sourceFolderPicker: null, createPasswordPrompt: null, fileSystemBrowser: new RootedBrowser(dir));

      TreeNodeItem rootNode = vm.FileSystemTree.Single();
      rootNode.IsExpanded = true;
      TreeNodeItem archiveNode = rootNode.Children.Single(c => c.Name == "data.7z");
      vm.SelectedTreeNode = archiveNode; // выбрали архив (не открывали)

      Assert.False(vm.HasArchive);
      Assert.True(vm.CanShowArchiveInfo);

      ArchiveInfo? info = await vm.BuildArchiveInfoAsync();
      Assert.NotNull(info);
      Assert.Equal("data.7z", info!.Name);
      Assert.Equal(2, info.FileCount);
      Assert.Equal(1, info.FolderCount);
      Assert.Equal(x.LongLength + y.LongLength, info.UncompressedSize);
      Assert.Equal(archiveBytes.LongLength, info.CompressedSize);
      Assert.True(info.Ratio > 1.0);
    }
    finally
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, recursive: true);
    }
  }

  [Fact]
  public void BuildArchiveInfo_БезАрхива_Null()
  {
    var vm = new MainViewModel(new StubPicker(null), new NullPasswordPrompt(), new StubFolderPicker());
    Assert.Null(vm.BuildArchiveInfo());
  }

  [Fact]
  public async Task BuildArchiveInfo_ОткрытыйАрхив_СчётчикиИРазмеры()
  {
    byte[] x = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("документ ", 500)));
    byte[] y = Encoding.UTF8.GetBytes("короткий файл");
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("docs/x.txt", x), new SevenZipArchiveWriterEntry("y.txt", y)],
        SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    var vm = new MainViewModel(new StubPicker(new PickedArchive("data.7z", archive)),
        new NullPasswordPrompt(), new StubFolderPicker());
    await vm.OpenCommand.ExecuteAsync();
    Assert.True(vm.HasArchive);

    ArchiveInfo? info = vm.BuildArchiveInfo();
    Assert.NotNull(info);
    Assert.Equal("data.7z", info!.Name);
    Assert.Equal(2, info.FileCount);
    Assert.Equal(1, info.FolderCount); // docs
    Assert.Equal(x.LongLength + y.LongLength, info.UncompressedSize);
    Assert.Equal(archive.LongLength, info.CompressedSize);

    // Текст сжимаемый → коэффициент > 1, показывается как «N×».
    Assert.True(info.Ratio > 1.0);
    Assert.EndsWith("×", info.RatioDisplay);
    Assert.NotEqual("—", info.SavedDisplay);
  }
}
