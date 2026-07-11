using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тест прямого потокового извлечения архива с диска из UI (ExtractArchiveFileCommand): архив
/// открывается по пути и распаковывается через сервис, без загрузки в память.
/// </summary>
public sealed class MainViewModelExtractArchiveFileTests
{
  private sealed class PathArchivePicker(string? path) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
    public Task<string?> PickArchivePathAsync() => Task.FromResult(path);
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

  [Fact]
  public async Task ИзвлечьАрхивСДиска_РаспаковываетФайлыНаДиск()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет-мир");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("UI извлечение 0123456789 ", 5000)));

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiExtractFile", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "in.7z");
    string outDir = Path.Combine(dir, "out");

    try
    {
      // Готовим реальный архив-файл на диске.
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

      await vm.ExtractArchiveFileCommand.ExecuteAsync();

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
