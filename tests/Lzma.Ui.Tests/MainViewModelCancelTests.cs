using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты отмены длительной операции: команда «Отмена» доступна во время операции, отменяет её
/// через CancellationToken, VM показывает «Операция отменена.» и сбрасывает состояние.
/// </summary>
public sealed class MainViewModelCancelTests
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

  private sealed class StubSourceFilesPicker(IReadOnlyList<PickedFile> files) : ISourceFilesPicker
  {
    public Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
        => Task.FromResult<IReadOnlyList<PickedFile>?>(files);
  }

  // Пикер, имитирующий долгое чтение: репортит скан-прогресс (зажигает индикатор),
  // затем «висит» до отмены токена и бросает OperationCanceledException.
  private sealed class ScanBlockingSourceFilesPicker : ISourceFilesPicker
  {
    public async Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
        IProgress<ScanProgress>? progress = null, CancellationToken token = default)
    {
      progress?.Report(new ScanProgress(1, 100)); // синхронно → IsScanning=true

      var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using (token.Register(() => gate.TrySetResult()))
        await gate.Task;

      token.ThrowIfCancellationRequested();
      return null;
    }
  }

  private sealed class StubSaveFilePicker(string? path) : ISaveFilePicker
  {
    public Task<string?> PickSavePathAsync(string suggestedFileName) => Task.FromResult(path);
  }

  // Сервис, чьё создание «висит» до отмены токена, затем бросает OperationCanceledException.
  private sealed class CancellableArchiveService : IArchiveService
  {
    public Task<ArchiveOpenOutcome> OpenAsync(byte[] bytes, string? password)
        => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));

    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(
        byte[] bytes, string? password, string destination,
        IProgress<SevenZipProgress>? progress = null, CancellationToken token = default)
        => Task.FromResult(SevenZipArchiveDecodeResult.Ok);

    public async Task<ArchiveCreateOutcome> CreateArchiveAsync(
        IReadOnlyList<SevenZipArchiveWriterEntry> entries,
        SevenZipWriterCompressionMethod method,
        IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default)
    {
      var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
      using (token.Register(() => gate.TrySetResult()))
        await gate.Task;

      token.ThrowIfCancellationRequested();
      return new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []);
    }

    public Task<bool> WriteArchiveAsync(byte[] archive, string path) => Task.FromResult(true);

    public Task<string> DescribeMethodsAsync(byte[] bytes, string? password) => Task.FromResult(string.Empty);
  }

  [Fact]
  public async Task Cancel_ВоВремяСоздания_ПрерываетИСообщает()
  {
    var vm = new MainViewModel(
        new StubArchivePicker(),
        new NullPasswordPrompt(),
        new StubFolderPicker(),
        new CancellableArchiveService(),
        new StubSourceFilesPicker([new PickedFile("a.txt", [1, 2, 3])]),
        new StubSaveFilePicker("out.7z"));

    // Стартуем создание — оно «зависнет» в сервисе до отмены.
    Task op = vm.CreateCommand.ExecuteAsync();

    // Операция идёт: команда отмены доступна.
    Assert.True(vm.IsOperating);
    Assert.True(vm.CancelCommand.CanExecute(null));

    // Отменяем.
    vm.CancelCommand.Execute(null);
    await op;

    Assert.Equal("Операция отменена.", vm.StatusMessage);
    Assert.False(vm.IsOperating);
    Assert.False(vm.CancelCommand.CanExecute(null)); // после завершения недоступна
  }

  [Fact]
  public void Cancel_КогдаНичегоНеИдёт_Недоступна()
  {
    var vm = new MainViewModel(new StubArchivePicker(), new NullPasswordPrompt(), new StubFolderPicker());

    Assert.False(vm.IsOperating);
    Assert.False(vm.CancelCommand.CanExecute(null));
    Assert.False(vm.IsCancelVisible);
  }

  [Fact]
  public async Task Cancel_ВоВремяСканирования_ПрерываетИСообщает()
  {
    var vm = new MainViewModel(
        new StubArchivePicker(),
        new NullPasswordPrompt(),
        new StubFolderPicker(),
        new CancellableArchiveService(),
        new ScanBlockingSourceFilesPicker(),
        new StubSaveFilePicker("out.7z"));

    // Стартуем создание — «зависнет» на фазе сканирования до отмены токена.
    Task op = vm.CreateCommand.ExecuteAsync();

    // Идёт сканирование: индикатор зажжён, кнопка отмены доступна и видима.
    Assert.True(vm.IsScanning);
    Assert.True(vm.CancelCommand.CanExecute(null));
    Assert.True(vm.IsCancelVisible);

    // Отменяем сканирование.
    vm.CancelCommand.Execute(null);
    await op;

    Assert.Equal("Операция отменена.", vm.StatusMessage);
    Assert.False(vm.IsScanning);
    Assert.Null(vm.ScanStatus);
    Assert.False(vm.CancelCommand.CanExecute(null));
  }
}
