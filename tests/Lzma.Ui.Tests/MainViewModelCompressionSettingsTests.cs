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
    public long CapturedVolumeSize = -1;
    public SevenZipWriterCompressionMethod CapturedMethod = (SevenZipWriterCompressionMethod)(-1);

    public Task<ArchiveOpenOutcome> OpenAsync(byte[] b, string? p) => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));
    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(byte[] b, string? p, string d, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default, IProgress<string>? cf = null) => Task.FromResult(SevenZipArchiveDecodeResult.Ok);
    public Task<ArchiveCreateOutcome> CreateArchiveAsync(IReadOnlyList<SevenZipArchiveWriterEntry> e, SevenZipWriterCompressionMethod m, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default) => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));
    public Task<bool> WriteArchiveAsync(byte[] a, string p) => Task.FromResult(true);
    public Task<string> DescribeMethodsAsync(byte[] b, string? p) => Task.FromResult(string.Empty);

    public Task<SevenZipArchiveWriteResult> CreateArchiveToFileAsync(
        IReadOnlyList<SevenZipStreamingEntry> entries, string destinationPath, SevenZipWriterCompressionMethod method,
        int dictionarySize, int maxDegreeOfParallelism = 0, IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default, IProgress<SevenZipCompressionFileProgress>? currentFile = null, long volumeSize = 0,
        string? password = null)
    {
      CapturedMethod = method;
      CapturedDict = dictionarySize;
      CapturedDop = maxDegreeOfParallelism;
      CapturedVolumeSize = volumeSize;
      return Task.FromResult(SevenZipArchiveWriteResult.Ok);
    }
  }

  [Fact]
  public void ЗначенияПоУмолчанию_Разумны()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());
    Assert.Equal(1 << 22, vm.SelectedDictionarySize); // 4 МБ
    Assert.Equal(0, vm.SelectedThreadCount);          // авто
    Assert.Equal(0, vm.SelectedVolumeSize);           // один файл, без томов
    Assert.NotEmpty(vm.ThreadCountOptions);
    Assert.NotEmpty(vm.DictionarySizeOptions);
    Assert.NotEmpty(vm.VolumeSizeOptions);
  }

  // Сервис, который во время «создания» репортит кодеки пофайлово через currentFile.
  private sealed class CodecReportingService(params string[] codecs) : IArchiveService
  {
    public Task<ArchiveOpenOutcome> OpenAsync(byte[] b, string? p) => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));
    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(byte[] b, string? p, string d, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default, IProgress<string>? cf = null) => Task.FromResult(SevenZipArchiveDecodeResult.Ok);
    public Task<ArchiveCreateOutcome> CreateArchiveAsync(IReadOnlyList<SevenZipArchiveWriterEntry> e, SevenZipWriterCompressionMethod m, IProgress<SevenZipProgress>? pr = null, CancellationToken t = default) => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));
    public Task<bool> WriteArchiveAsync(byte[] a, string p) => Task.FromResult(true);
    public Task<string> DescribeMethodsAsync(byte[] b, string? p) => Task.FromResult(string.Empty);

    public Task<SevenZipArchiveWriteResult> CreateArchiveToFileAsync(
        IReadOnlyList<SevenZipStreamingEntry> entries, string destinationPath, SevenZipWriterCompressionMethod method,
        int dictionarySize, int maxDegreeOfParallelism = 0, IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default, IProgress<SevenZipCompressionFileProgress>? currentFile = null, long volumeSize = 0,
        string? password = null)
    {
      // Синхронный репорт кодеков (как в ядре — до возврата), чтобы VM набрал точные счётчики.
      for (int i = 0; i < codecs.Length; i++)
        currentFile?.Report(new SevenZipCompressionFileProgress($"f{i}", codecs[i]));

      return Task.FromResult(SevenZipArchiveWriteResult.Ok);
    }
  }

  [Fact]
  public void РазбивкаКодеков_ПорядокИПропускНулей()
  {
    var counts = new Dictionary<string, int> { ["Copy"] = 8, ["PPMd"] = 12, ["LZMA2"] = 40 };
    Assert.Equal("Авто: PPMd — 12, LZMA2 — 40, Copy — 8", MainViewModel.FormatCodecBreakdown(counts));

    Assert.Equal("Авто: PPMd — 3", MainViewModel.FormatCodecBreakdown(new Dictionary<string, int> { ["PPMd"] = 3, ["LZMA2"] = 0 }));
    Assert.Equal(string.Empty, MainViewModel.FormatCodecBreakdown(new Dictionary<string, int>()));
  }

  [Fact]
  public async Task Авто_ИтогСодержитРазбивкуКодеков()
  {
    var refs = new List<PickedFileRef> { new("a", 1, () => new MemoryStream([1])) };
    var vm = new MainViewModel(
        new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
        new CodecReportingService("PPMd", "PPMd", "LZMA2", "Copy"), new RefsPicker(refs), new StubSavePicker("out.7z"))
    {
      SelectedCompressionMethod = SevenZipWriterCompressionMethod.Auto,
    };

    await vm.CreateCommand.ExecuteAsync();

    Assert.Contains("Авто: PPMd — 2, LZMA2 — 1, Copy — 1", vm.StatusMessage);
  }

  [Fact]
  public async Task НеАвто_РазбивкиКодековНет()
  {
    var refs = new List<PickedFileRef> { new("a", 1, () => new MemoryStream([1])) };
    var vm = new MainViewModel(
        new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker(),
        new CodecReportingService("LZMA2", "LZMA2"), new RefsPicker(refs), new StubSavePicker("out.7z"))
    {
      SelectedCompressionMethod = SevenZipWriterCompressionMethod.Lzma2,
    };

    await vm.CreateCommand.ExecuteAsync();

    Assert.DoesNotContain("Авто:", vm.StatusMessage);
  }

  [Fact]
  public void НижняяПанель_СкрытаБезОперацииИСтатуса_ВидимаПриСтатусе()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());

    // Ничего не идёт и статуса нет — панель скрыта (не занимает место).
    Assert.False(vm.IsBottomBarVisible);

    bool raised = false;
    vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsBottomBarVisible)) raised = true; };

    // Появился статус — панель видима, и об изменении видимости уведомили.
    vm.StatusMessage = "Готово";
    Assert.True(vm.IsBottomBarVisible);
    Assert.True(raised);

    // Статус убрали — снова скрыта.
    vm.StatusMessage = null;
    Assert.False(vm.IsBottomBarVisible);

    // Идёт операция — видима даже без статуса.
    vm.IsBusy = true;
    Assert.True(vm.IsBottomBarVisible);
  }

  [Fact]
  public void НижняяПанель_СкрытаПокаОткрытоОкноОперации_НеДублируетПрогресс()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());
    vm.StatusMessage = "Готово";
    Assert.True(vm.IsBottomBarVisible);

    bool raised = false;
    vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsBottomBarVisible)) raised = true; };

    // Открыто модальное окно операции — прогресс показывается в нём, нижняя панель главного окна скрыта.
    vm.IsOperationWindowActive = true;
    Assert.False(vm.IsBottomBarVisible);
    Assert.True(raised);

    // Окно закрыли — финальный статус снова виден в главном окне.
    vm.IsOperationWindowActive = false;
    Assert.True(vm.IsBottomBarVisible);
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
      SelectedVolumeSize = 100L << 20,  // 100 МБ тома
    };

    await vm.CreateCommand.ExecuteAsync();

    Assert.Equal(1 << 24, svc.CapturedDict);
    Assert.Equal(4, svc.CapturedDop);
    Assert.Equal(100L << 20, svc.CapturedVolumeSize);
  }
}
