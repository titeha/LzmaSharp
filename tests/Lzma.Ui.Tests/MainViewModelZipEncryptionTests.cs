using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.Zip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Шифрование ZIP из UI (WinZip-AES): создать зашифрованный ZIP (галка EncryptZip + пароль-диалог) и
/// извлечь его обратно через VM (детект шифрования → запрос пароля → расшифровка).
/// </summary>
public sealed class MainViewModelZipEncryptionTests
{
  private sealed class PathArchivePicker(string? path) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
    public Task<string?> PickArchivePathAsync() => Task.FromResult(path);
  }

  private sealed class FixedPasswordPrompt(string? password) : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string a, bool b) => Task.FromResult(password);
  }

  private sealed class StubFolderPicker(string? folder) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
  }

  private sealed class RefsPicker(IReadOnlyList<PickedFileRef> refs) : ISourceFilesPicker
  {
    public bool SupportsRefs => true;
    public Task<IReadOnlyList<PickedFileRef>?> PickFileRefsAsync(IProgress<ScanProgress>? p = null, CancellationToken t = default)
        => Task.FromResult<IReadOnlyList<PickedFileRef>?>(refs);
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(IProgress<ScanProgress>? p = null, CancellationToken t = default)
        => throw new InvalidOperationException("streaming");
  }

  private sealed class StubSave(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string s) => Task.FromResult(path);
  }

  private sealed class CreatePwPrompt(string? password) : ICreatePasswordPrompt
  {
    public Task<string?> RequestNewPasswordAsync() => Task.FromResult(password);
  }

  [Fact]
  public async Task СоздатьЗашифрованныйZip_ИзвлечьСПаролем_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes("секрет-1 " + string.Concat(System.Linq.Enumerable.Repeat("текст ", 200)));
    byte[] b = Encoding.UTF8.GetBytes("секрет-2");

    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiZipEnc", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string zipPath = Path.Combine(dir, "enc.zip");
    string outDir = Path.Combine(dir, "out");
    const string pw = "S3cret!пароль";

    try
    {
      var refs = new List<PickedFileRef>
      {
        new("docs/a.txt", a.LongLength, () => new MemoryStream(a)),
        new("b.txt", b.LongLength, () => new MemoryStream(b)),
      };

      // Создание зашифрованного ZIP через VM.
      var createVm = new MainViewModel(
          new PathArchivePicker(null), new FixedPasswordPrompt(null), new StubFolderPicker(null),
          new LzmaArchiveService(), new RefsPicker(refs), new StubSave(zipPath),
          sourceFolderPicker: null, createPasswordPrompt: new CreatePwPrompt(pw))
      {
        UseZipFormat = true,
        EncryptZip = true,
      };

      await createVm.CreateCommand.ExecuteAsync();
      Assert.True(File.Exists(zipPath));

      // Каталог помечен зашифрованным.
      using (var read = new FileStream(zipPath, FileMode.Open, FileAccess.Read))
      {
        Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
        Assert.All(entries, e => Assert.True(e.IsEncrypted));
      }

      // Извлечение через VM: детект шифрования → запрос пароля (FixedPasswordPrompt) → расшифровка.
      var extractVm = new MainViewModel(
          new PathArchivePicker(zipPath), new FixedPasswordPrompt(pw), new StubFolderPicker(outDir),
          new LzmaArchiveService());

      await extractVm.ExtractArchiveFileCommand.ExecuteAsync();

      Assert.Equal($"Извлечено в: {outDir}", extractVm.StatusMessage);
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(outDir, "docs", "a.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(outDir, "b.txt")));
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }

  [Fact]
  public async Task ИзвлечьЗашифрованный_НеверныйПароль_Сообщение()
  {
    byte[] a = Encoding.UTF8.GetBytes("данные");
    string dir = Path.Combine(Path.GetTempPath(), "LzmaUiZipEncBad", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string zipPath = Path.Combine(dir, "enc.zip");
    string outDir = Path.Combine(dir, "out");

    try
    {
      var svc = new LzmaArchiveService();
      var refs = new List<PickedFileRef> { new("a.txt", a.LongLength, () => new MemoryStream(a)) };
      var createVm = new MainViewModel(
          new PathArchivePicker(null), new FixedPasswordPrompt(null), new StubFolderPicker(null),
          svc, new RefsPicker(refs), new StubSave(zipPath), null, new CreatePwPrompt("right"))
      { UseZipFormat = true, EncryptZip = true };
      await createVm.CreateCommand.ExecuteAsync();

      var extractVm = new MainViewModel(
          new PathArchivePicker(zipPath), new FixedPasswordPrompt("wrong"), new StubFolderPicker(outDir), svc);
      await extractVm.ExtractArchiveFileCommand.ExecuteAsync();

      Assert.Contains("Неверный пароль", extractVm.StatusMessage);
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }
}
