using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тест обзора большого архива по пути (OpenArchiveFileCommand): содержимое читается листингом без
/// распаковки, строится дерево, а последующее «Извлечь всё» идёт потоковым путём из файла.
/// </summary>
public sealed class MainViewModelOpenArchiveFileTests
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
  public async Task ОткрытьБольшойАрхив_ДеревоИИзвлечение()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("обзор 0123456789 ", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiOpenBig", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "in.7z");
    string outDir = Path.Combine(dir, "out");

    try
    {
      var entries = new List<SevenZipStreamingEntry>
      {
        new("a.txt", a.LongLength, () => new MemoryStream(a)),
        new("sub/big.bin", big.LongLength, () => new MemoryStream(big)),
      };
      using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite))
        Assert.Equal(SevenZipArchiveWriteResult.Ok,
            SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, fs, 1 << 20));

      var vm = new MainViewModel(
          new PathArchivePicker(archivePath),
          new NullPasswordPrompt(),
          new StubFolderPicker(outDir),
          new LzmaArchiveService());

      // Обзор без распаковки.
      await vm.OpenArchiveFileCommand.ExecuteAsync();

      Assert.True(vm.HasArchive);
      var names = vm.Items.Select(i => i.Name).ToList();
      Assert.Contains("a.txt", names);
      Assert.Contains("sub", names); // папка из пути sub/big.bin

      // Извлечение идёт потоковым путём из файла.
      await vm.ExtractAllCommand.ExecuteAsync();

      Assert.Equal($"Извлечено в: {outDir}", vm.StatusMessage);
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(outDir, "a.txt")));
      Assert.Equal(big, File.ReadAllBytes(Path.Combine(outDir, "sub", "big.bin")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
