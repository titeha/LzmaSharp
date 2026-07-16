using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// AES-3 (VM): потоковое «Извлечь архив с диска…» для зашифрованного архива спрашивает пароль и
/// пробрасывает его в сервис; отмена пароля — не извлекает.
/// </summary>
public sealed class MainViewModelStreamingExtractAesTests
{
  private sealed class PathPicker(string? path) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
    public Task<string?> PickArchivePathAsync() => Task.FromResult(path);
  }

  private sealed class FolderPicker(string? folder) : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult(folder);
  }

  private sealed class ScriptedPasswordPrompt(string? password) : IPasswordPrompt
  {
    public int Calls;
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
    {
      Calls++;
      return Task.FromResult(password);
    }
  }

  private sealed class EncryptedCapturingService(bool encrypted) : IArchiveService
  {
    public string? CapturedPassword = "<none>";
    public bool ExtractCalled;

    public Task<bool> IsArchiveEncryptedAsync(string archivePath) => Task.FromResult(encrypted);

    public Task<SevenZipArchiveDecodeResult> ExtractArchiveFileAsync(
        string archivePath, string destination, IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default, IProgress<string>? currentFile = null, string? password = null)
    {
      ExtractCalled = true;
      CapturedPassword = password;
      return Task.FromResult(SevenZipArchiveDecodeResult.Ok);
    }

    public Task<ArchiveOpenOutcome> OpenAsync(byte[] b, string? p) => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));
    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(byte[] b, string? p, string d, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default, IProgress<string>? cf = null) => Task.FromResult(SevenZipArchiveDecodeResult.Ok);
    public Task<ArchiveCreateOutcome> CreateArchiveAsync(IReadOnlyList<SevenZipArchiveWriterEntry> e, SevenZipWriterCompressionMethod m, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default) => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));
    public Task<bool> WriteArchiveAsync(byte[] a, string p) => Task.FromResult(true);
    public Task<string> DescribeMethodsAsync(byte[] b, string? p) => Task.FromResult(string.Empty);
  }

  [Fact]
  public async Task ЗашифрованныйАрхив_СпрашиваетПарольИПробрасывает()
  {
    var svc = new EncryptedCapturingService(encrypted: true);
    var prompt = new ScriptedPasswordPrompt("secret-pw");

    var vm = new MainViewModel(
        new PathPicker(@"C:\enc.7z"), prompt, new FolderPicker(@"C:\out"), svc);

    await vm.ExtractArchiveFileCommand.ExecuteAsync();

    Assert.Equal(1, prompt.Calls);
    Assert.True(svc.ExtractCalled);
    Assert.Equal("secret-pw", svc.CapturedPassword);
  }

  [Fact]
  public async Task ЗашифрованныйАрхив_ОтменаПароля_НеИзвлекает()
  {
    var svc = new EncryptedCapturingService(encrypted: true);
    var prompt = new ScriptedPasswordPrompt(null); // отмена

    var vm = new MainViewModel(
        new PathPicker(@"C:\enc.7z"), prompt, new FolderPicker(@"C:\out"), svc);

    await vm.ExtractArchiveFileCommand.ExecuteAsync();

    Assert.Equal(1, prompt.Calls);
    Assert.False(svc.ExtractCalled);
    Assert.Contains("отменено", vm.StatusMessage);
  }

  [Fact]
  public async Task НезашифрованныйАрхив_ПарольНеСпрашивается()
  {
    var svc = new EncryptedCapturingService(encrypted: false);
    var prompt = new ScriptedPasswordPrompt("unused");

    var vm = new MainViewModel(
        new PathPicker(@"C:\plain.7z"), prompt, new FolderPicker(@"C:\out"), svc);

    await vm.ExtractArchiveFileCommand.ExecuteAsync();

    Assert.Equal(0, prompt.Calls);
    Assert.True(svc.ExtractCalled);
    Assert.Null(svc.CapturedPassword);
  }
}
