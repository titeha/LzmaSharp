using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты прогресса с процентами: чистое преобразование отчёта ядра в проценты и
/// проброс его в свойство <see cref="MainViewModel.ProgressPercent"/> (с уведомлением).
/// </summary>
public sealed class MainViewModelProgressTests
{
  private sealed class StubArchivePicker : IArchivePicker
  {
    public Task<PickedArchive?> PickAsync() => Task.FromResult<PickedArchive?>(null);
  }

  private sealed class StubPasswordPrompt : IPasswordPrompt
  {
    public Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
        => Task.FromResult<string?>(null);
  }

  private sealed class StubFolderPicker : IFolderPicker
  {
    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
  }

  private static MainViewModel BuildViewModel()
      => new(new StubArchivePicker(), new StubPasswordPrompt(), new StubFolderPicker());

  [Theory]
  [InlineData(0, 0, 0.0)]       // ничего не сделано
  [InlineData(50, 100, 50.0)]   // половина
  [InlineData(100, 100, 100.0)] // готово
  [InlineData(3, 4, 75.0)]      // дробь
  public void ToPercent_ReturnsShareOfTotal(long processed, long total, double expected)
  {
    double percent = MainViewModel.ToPercent(new SevenZipProgress(processed, total));

    Assert.Equal(expected, percent, precision: 6);
  }

  [Theory]
  [InlineData(0, 0)]    // неизвестный объём → 0 %
  [InlineData(10, 0)]   // total <= 0 → 0 %
  [InlineData(10, -5)]  // отрицательный total → 0 %
  public void ToPercent_UnknownTotal_IsZero(long processed, long total)
  {
    Assert.Equal(0.0, MainViewModel.ToPercent(new SevenZipProgress(processed, total)));
  }

  [Theory]
  [InlineData(150, 100)] // переотчёт сверх объёма
  public void ToPercent_OverReport_ClampedTo100(long processed, long total)
  {
    Assert.Equal(100.0, MainViewModel.ToPercent(new SevenZipProgress(processed, total)));
  }

  [Fact]
  public void ReportProgress_UpdatesProperty_AndRaisesNotification()
  {
    MainViewModel vm = BuildViewModel();
    bool notified = false;
    vm.PropertyChanged += (_, e) =>
    {
      if (e.PropertyName == nameof(MainViewModel.ProgressPercent))
        notified = true;
    };

    vm.ReportProgress(new SevenZipProgress(25, 100));

    Assert.Equal(25.0, vm.ProgressPercent);
    Assert.True(notified);
  }

  [Fact]
  public void ReportProgress_ОбновляетТекстОбъёма()
  {
    MainViewModel vm = BuildViewModel();

    vm.ReportProgress(new SevenZipProgress(3 * 1024 * 1024, 10 * 1024 * 1024));

    Assert.Equal("3 МБ / 10 МБ", vm.ProgressText);
  }

  [Theory]
  [InlineData(0, 0, "")]                       // неизвестный объём → пусто
  [InlineData(10, 0, "")]                      // total <= 0 → пусто
  [InlineData(512, 1024, "512 Б / 1 КБ")]      // байты/КБ
  public void FormatProgressText_ФорматируетОбъём(long processed, long total, string expected)
  {
    Assert.Equal(expected, MainViewModel.FormatProgressText(new SevenZipProgress(processed, total)));
  }
}
