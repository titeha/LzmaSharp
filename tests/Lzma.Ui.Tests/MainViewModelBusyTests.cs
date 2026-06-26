using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты отложенного индикатора занятости: быстрые операции его не показывают,
/// длительные — показывают только после порога <see cref="MainViewModel.BusyIndicatorDelay"/>.
/// </summary>
public sealed class MainViewModelBusyTests
{
  // Управляемый сервис: извлечение можно «подвесить» через шлюз, чтобы смоделировать долгую операцию.
  private sealed class GatedArchiveService : IArchiveService
  {
    public TaskCompletionSource? ExtractGate { get; set; }

    public Task<ArchiveOpenOutcome> OpenAsync(byte[] bytes, string? password)
        => Task.FromResult(new ArchiveOpenOutcome(SevenZipArchiveDecodeResult.Ok, []));

    public async Task<SevenZipArchiveDecodeResult> ExtractAllAsync(byte[] bytes, string? password, string destination)
    {
      if (ExtractGate is not null)
        await ExtractGate.Task;

      return SevenZipArchiveDecodeResult.Ok;
    }

    public Task<ArchiveCreateOutcome> CreateArchiveAsync(
        IReadOnlyList<SevenZipArchiveWriterEntry> entries,
        SevenZipWriterCompressionMethod method)
        => Task.FromResult(new ArchiveCreateOutcome(SevenZipArchiveWriteResult.Ok, []));

    public Task<bool> WriteArchiveAsync(byte[] archive, string path) => Task.FromResult(true);

    public Task<string> DescribeMethodsAsync(byte[] bytes, string? password) => Task.FromResult(string.Empty);
  }

  private sealed class StubArchivePicker(PickedArchive? result) : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult(result);
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

  private static MainViewModel BuildOpenedViewModel(GatedArchiveService service)
  {
    var vm = new MainViewModel(
        new StubArchivePicker(new PickedArchive("a.7z", [1, 2, 3])),
        new NullPasswordPrompt(),
        new StubFolderPicker("dest"),
        service);

    return vm;
  }

  private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
  {
    var sw = Stopwatch.StartNew();

    while (!condition() && sw.Elapsed < timeout)
      await Task.Delay(10);
  }

  [Fact]
  public async Task БыстраяОперация_ИндикаторНеПоказывается()
  {
    var service = new GatedArchiveService(); // шлюз null → извлечение мгновенно
    MainViewModel vm = BuildOpenedViewModel(service);
    vm.BusyIndicatorDelay = TimeSpan.FromSeconds(30); // порог заведомо больше операции

    bool everBusy = false;
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(MainViewModel.IsBusy) && vm.IsBusy)
        everBusy = true;
    };

    await vm.OpenCommand.ExecuteAsync();
    await vm.ExtractAllCommand.ExecuteAsync();

    Assert.False(everBusy);          // индикатор ни разу не зажёгся
    Assert.False(vm.IsBusy);
    Assert.False(vm.IsOperating);
  }

  [Fact]
  public async Task ДлительнаяОперация_ПослеПорога_ИндикаторПоказывается()
  {
    var gate = new TaskCompletionSource();
    var service = new GatedArchiveService { ExtractGate = gate };
    MainViewModel vm = BuildOpenedViewModel(service);
    vm.BusyIndicatorDelay = TimeSpan.Zero; // порог 0 → показать сразу, как только операция «зависла»

    await vm.OpenCommand.ExecuteAsync();

    Task extract = vm.ExtractAllCommand.ExecuteAsync(); // не дожидаемся — операция висит на шлюзе

    Assert.True(vm.IsOperating); // защита от повторного запуска включается сразу

    await WaitUntilAsync(() => vm.IsBusy, TimeSpan.FromSeconds(5));
    Assert.True(vm.IsBusy); // порог пройден → индикатор виден

    gate.SetResult(); // операция завершилась
    await extract;

    Assert.False(vm.IsBusy);
    Assert.False(vm.IsOperating);
  }

  [Fact]
  public async Task ВоВремяОперации_ПовторныйЗапускЗаблокирован()
  {
    var gate = new TaskCompletionSource();
    var service = new GatedArchiveService { ExtractGate = gate };
    MainViewModel vm = BuildOpenedViewModel(service);

    await vm.OpenCommand.ExecuteAsync();

    Task extract = vm.ExtractAllCommand.ExecuteAsync();

    Assert.True(vm.IsOperating);
    Assert.False(vm.ExtractAllCommand.CanExecute(null)); // нельзя запустить повторно

    gate.SetResult();
    await extract;

    Assert.True(vm.ExtractAllCommand.CanExecute(null)); // снова доступно
  }
}
