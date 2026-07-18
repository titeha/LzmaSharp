using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Обратная связь при открытии: пока идёт обзор/декод архива, <see cref="MainViewModel.IsOpening"/>
/// взведён (индикатор занятости показывается сразу), а по завершении гаснет.
/// </summary>
public sealed class MainViewModelOpenFeedbackTests
{
  // Сервис со «шлюзом» на OpenAsync — держит открытие, пока тест не отпустит.
  private sealed class GatedOpenService : IArchiveService
  {
    public TaskCompletionSource Gate { get; } = new();

    public async Task<ArchiveOpenOutcome> OpenAsync(byte[] bytes, string? password)
    {
      await Gate.Task;
      return new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []);
    }

    public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(
        byte[] bytes, string? password, string destination,
        System.IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default,
        System.IProgress<string>? currentFile = null)
        => Task.FromResult(SevenZipArchiveDecodeResult.Ok);

    public Task<ArchiveCreateOutcome> CreateArchiveAsync(
        IReadOnlyList<SevenZipArchiveWriterEntry> entries,
        SevenZipWriterCompressionMethod method,
        System.IProgress<SevenZipProgress>? progress = null,
        CancellationToken token = default)
        => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));

    public Task<bool> WriteArchiveAsync(byte[] archive, string path) => Task.FromResult(true);

    public Task<string> DescribeMethodsAsync(byte[] bytes, string? password) => Task.FromResult(string.Empty);
  }

  private sealed class BytesArchivePicker(byte[] bytes) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(new PickedArchive("in.7z", bytes));
    public Task<string?> PickArchivePathAsync() => Task.FromResult<string?>(null);
  }

  private sealed class NullPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string a, bool b) => Task.FromResult<string?>(null);
  }

  private sealed class NullFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  [Fact]
  public async Task Открытие_ПокаИдёт_ИндикаторВзведён()
  {
    // Настоящий 7z, чтобы путь пошёл через OpenAsync (а не ZIP).
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("a.txt", Encoding.UTF8.GetBytes("hi"))], out byte[] archive));

    var service = new GatedOpenService();
    var vm = new MainViewModel(new BytesArchivePicker(archive), new NullPasswordPrompt(), new NullFolderPicker(), service);

    Assert.False(vm.IsOpening);

    // Всё до «шлюза» выполняется синхронно → к возврату задачи индикатор уже взведён.
    Task open = vm.OpenCommand.ExecuteAsync();
    Assert.True(vm.IsOpening);
    Assert.True(vm.IsBottomBarVisible);

    service.Gate.SetResult();
    await open;

    Assert.False(vm.IsOpening);
    Assert.True(vm.HasArchive);
  }
}
