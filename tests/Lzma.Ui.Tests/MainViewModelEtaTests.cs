using System;
using System.Threading.Tasks;

using Lzma.Ui.Services;
using Lzma.Core.SevenZip;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты оценки оставшегося времени (ETA): чистый расчёт по средней скорости
/// (<see cref="MainViewModel.EstimateRemaining"/>) и его форматирование
/// (<see cref="MainViewModel.FormatRemaining"/>).
/// </summary>
public sealed class MainViewModelEtaTests
{
  [Fact]
  public void EstimateRemaining_ПоловинаЗаДесятьСекунд_ЕщёДесять()
  {
    // Обработана половина за 10 с → на вторую половину при той же скорости нужно ещё 10 с.
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(50, 100), TimeSpan.FromSeconds(10));

    Assert.NotNull(eta);
    Assert.Equal(10.0, eta!.Value.TotalSeconds, precision: 6);
  }

  [Fact]
  public void EstimateRemaining_ЧетвертьЗаДесятьСекунд_ЕщёТридцать()
  {
    // Обработана четверть за 10 с → на оставшиеся три четверти нужно ещё 30 с.
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(25, 100), TimeSpan.FromSeconds(10));

    Assert.NotNull(eta);
    Assert.Equal(30.0, eta!.Value.TotalSeconds, precision: 6);
  }

  [Theory]
  [InlineData(0, 100, 10)]   // ничего не обработано → скорость неизвестна
  [InlineData(50, 0, 10)]    // неизвестен общий объём
  [InlineData(50, -5, 10)]   // отрицательный объём
  [InlineData(50, 100, 0)]   // ещё не прошло времени
  public void EstimateRemaining_НедостаточноДанных_Null(long processed, long total, double elapsedSeconds)
  {
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(processed, total), TimeSpan.FromSeconds(elapsedSeconds));

    Assert.Null(eta);
  }

  [Fact]
  public void EstimateRemaining_ВсёОбработано_Ноль()
  {
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(100, 100), TimeSpan.FromSeconds(10));

    Assert.NotNull(eta);
    Assert.Equal(TimeSpan.Zero, eta!.Value);
  }

  [Fact]
  public void EstimateRemaining_ПереотчётСверхОбъёма_Ноль()
  {
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(150, 100), TimeSpan.FromSeconds(10));

    Assert.NotNull(eta);
    Assert.Equal(TimeSpan.Zero, eta!.Value);
  }

  [Fact]
  public void EstimateRemaining_КрошечнаяДоля_НеПереполняется()
  {
    // 1 байт из long.MaxValue за 1 с — расчётное «осталось» огромно; не должно бросать.
    TimeSpan? eta = MainViewModel.EstimateRemaining(
        new SevenZipProgress(1, long.MaxValue), TimeSpan.FromSeconds(1));

    Assert.NotNull(eta);
    Assert.Equal(TimeSpan.MaxValue, eta!.Value);
  }

  [Theory]
  [InlineData(5, "осталось ~5 с")]              // только секунды
  [InlineData(0, "осталось ~0 с")]              // ноль
  [InlineData(65, "осталось ~1 мин 5 с")]       // минуты и секунды
  [InlineData(125, "осталось ~2 мин 5 с")]      // минуты и секунды
  [InlineData(3600, "осталось ~1 ч 0 мин")]     // ровно час
  [InlineData(3900, "осталось ~1 ч 5 мин")]     // часы и минуты (секунды опускаем)
  public void FormatRemaining_Форматирует(double totalSeconds, string expected)
  {
    Assert.Equal(expected, MainViewModel.FormatRemaining(TimeSpan.FromSeconds(totalSeconds)));
  }

  [Fact]
  public void FormatRemaining_Отрицательное_КакНоль()
  {
    Assert.Equal("осталось ~0 с", MainViewModel.FormatRemaining(TimeSpan.FromSeconds(-5)));
  }

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

  [Fact]
  public void ReportProgress_ЗаполняетEta_ПоИстёкшемуВремени()
  {
    MainViewModel vm = BuildViewModel();

    // Половина за 10 с → осталось ещё ~10 с.
    vm.ReportProgress(new SevenZipProgress(50, 100), TimeSpan.FromSeconds(10));

    Assert.Equal("осталось ~10 с", vm.ProgressEta);
  }

  [Fact]
  public void ReportProgress_НедостаточноДанных_EtaПусто()
  {
    MainViewModel vm = BuildViewModel();

    // Ничего не обработано → скорость неизвестна → ETA не показываем.
    vm.ReportProgress(new SevenZipProgress(0, 100), TimeSpan.FromSeconds(10));

    Assert.Null(vm.ProgressEta);
  }
}
