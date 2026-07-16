using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты проброса настроек сжатия (число потоков + размер словаря) из VM в сервис создания,
/// и разумных значений по умолчанию.
/// </summary>
public sealed class MainViewModelCompressionSettingsTests
{
  private sealed class StubArchivePicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string a, bool b) => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  private sealed class RefsPicker(IReadOnlyList<PickedFileRef> refs) : ISourceFilesPicker
  {
    public bool SupportsRefs => true;
    public Task<IReadOnlyList<PickedFileRef>?> PickFileRefsAsync(IProgress<ScanProgress>? p = null, CancellationToken t = default)
        => Task.FromResult<IReadOnlyList<PickedFileRef>?>(refs);
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(IProgress<ScanProgress>? p = null, CancellationToken t = default)
        => throw new InvalidOperationException();
  }

  private sealed class StubSavePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string s) => Task.FromResult(path);
  }

  // Сервис, ловящий переданные словарь и число потоков.
  private sealed class CapturingService : IArchiveService
  {
    public int CapturedDict = -1;
    public int CapturedDop = -1;
    public SevenZipWriterCompressionMethod CapturedMethod = (SevenZipWriterCompressionMethod)(-1);

    public Task<ArchiveOpenOutcome> OpenAsync(byte[] b, string? p) => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));
    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(byte[] b, string? p, string d, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default) => Task.FromResult(SevenZipArchiveDecodeResult.Ok);
    public Task<ArchiveCreateOutcome> CreateArchiveAsync(IReadOnlyList<SevenZipArchiveWriterEntry> e, SevenZipWriterCompressionMethod m, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default) => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));
    public Task<bool> WriteArchiveAsync(byte[] a, string p) => Task.FromResult(true);
    public Task<string> DescribeMethodsAsync(byte[] b, string? p) => Task.FromResult(string.Empty);

    public Task<SevenZipArchiveWriteResult> CreateArchiveToFileAsync(
        IReadOnlyList<SevenZipStreamingEntry> entries, string destinationPath, SevenZipWriterCompressionMethod method,
        int dictionarySize, int maxDegreeOfParallelism = 0, IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default, IProgress<string>? currentFile = null)
    {
      CapturedMethod = method;
      CapturedDict = dictionarySize;
      CapturedDop = maxDegreeOfParallelism;
      return Task.FromResult(SevenZipArchiveWriteResult.Ok);
    }
  }

  [Fact]
  public void ЗначенияПоУмолчанию_Разумны()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());
    Assert.Equal(1 << 22, vm.SelectedDictionarySize); // 4 МБ
    Assert.Equal(0, vm.SelectedThreadCount);          // авто
    Assert.NotEmpty(vm.ThreadCountOptions);
    Assert.NotEmpty(vm.DictionarySizeOptions);
  }

  [Fact]
  public async Task Настройки_ПрокидываютсяВСервис()
  {
    var svc = new CapturingService();
    var refs = new List<PickedFileRef> { new("a.txt", 3, () => new MemoryStream([1, 2, 3])) };

    var vm = new MainViewModel(
        new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
        svc, new RefsPicker(refs), new StubSavePicker("out.7z"))
    {
      SelectedDictionarySize = 1 << 24, // 16 МБ
      SelectedThreadCount = 4,
    };

    await vm.CreateCommand.ExecuteAsync();

    Assert.Equal(1 << 24, svc.CapturedDict);
    Assert.Equal(4, svc.CapturedDop);
  }
}
