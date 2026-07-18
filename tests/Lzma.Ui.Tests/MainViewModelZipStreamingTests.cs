using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Lzma.Core.Zip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Потоковое открытие/извлечение большого ZIP по пути через VM (детект формата → обзор каталога →
/// потоковое извлечение из файла, без загрузки архива в память).
/// </summary>
public sealed class MainViewModelZipStreamingTests
{
  private sealed class PathArchivePicker(string? path) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
    public Task<string?> PickArchivePathAsync() => Task.FromResult(path);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string a, bool b) => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker(string? folder) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
  }

  [Fact]
  public async Task ОткрытьБольшойZip_ДеревоИПотоковоеИзвлечение()
  {
    byte[] text = Encoding.UTF8.GetBytes("zip streaming through view model");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum. ", 30_000))); // > окна инфлейтера

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiZip", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "in.zip");
    string outDir = Path.Combine(dir, "out");

    try
    {
      Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
      [
          new ZipWriterEntry("readme.txt", text),
          new ZipWriterEntry("sub/big.txt", big),
      ], out byte[] archive));
      File.WriteAllBytes(archivePath, archive);

      var vm = new MainViewModel(
          new PathArchivePicker(archivePath),
          new NullPasswordPrompt(),
          new StubFolderPicker(outDir),
          new LzmaArchiveService());

      // «Открыть большой архив…» распознаёт ZIP и строит дерево без распаковки.
      await vm.OpenArchiveFileCommand.ExecuteAsync();

      Assert.True(vm.HasArchive);
      var names = vm.Items.Select(i => i.Name).ToList();
      Assert.Contains("readme.txt", names);
      Assert.Contains("sub", names);

      // «Извлечь всё» идёт потоковым ZIP-путём прямо из файла.
      await vm.ExtractAllCommand.ExecuteAsync();

      Assert.Equal($"Извлечено в: {outDir}", vm.StatusMessage);
      Assert.Equal(text, File.ReadAllBytes(Path.Combine(outDir, "readme.txt")));
      Assert.Equal(big, File.ReadAllBytes(Path.Combine(outDir, "sub", "big.txt")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public async Task ИзвлечьZipСДиска_ПотоковоеИзвлечениеБезОткрытия()
  {
    byte[] payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("payload line\n", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiZipX", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "disk.zip");
    string outDir = Path.Combine(dir, "out");

    try
    {
      Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
          [new ZipWriterEntry("folder/data.txt", payload)], out byte[] archive));
      File.WriteAllBytes(archivePath, archive);

      var vm = new MainViewModel(
          new PathArchivePicker(archivePath),
          new NullPasswordPrompt(),
          new StubFolderPicker(outDir),
          new LzmaArchiveService());

      await vm.ExtractArchiveFileCommand.ExecuteAsync();

      Assert.Equal($"Извлечено в: {outDir}", vm.StatusMessage);
      Assert.Equal(payload, File.ReadAllBytes(Path.Combine(outDir, "folder", "data.txt")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
