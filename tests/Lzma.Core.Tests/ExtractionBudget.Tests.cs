using Xunit;

namespace Lzma.Core.Tests;

/// <summary>
/// Pure-инварианты для бюджета операции извлечения (SEC-004).
/// <para>
/// Тесты изолированы: без файловой системы, потоков, fixture-файлов, wall-clock и больших
/// аллокаций — только счётчики внутри <see cref="ExtractionBudget"/>. Тип internal и доступен
/// тестам через <c>InternalsVisibleTo</c> в <c>src/Lzma.Core/Lzma.Core.csproj</c>.
/// </para>
/// <para>
/// Тип в этом шаге НЕ подключён к production-маршрутам извлечения, поэтому эти тесты проверяют
/// только сам контракт счётчика, а поведение библиотеки не меняют.
/// </para>
/// </summary>
public sealed class ExtractionBudgetTests
{
  /// <summary>Отказ ровно на пределе: ровно в пределах выдаётся, следующий байт — нет.</summary>
  [Fact]
  public void Письмо_РовноНаПределе_Успешно_СледующийБайт_Отказано()
  {
    var budget = new ExtractionBudget(10, 1);

    Assert.True(budget.TryWrite(10));
    Assert.Equal(10, budget.WrittenBytes);
    Assert.Equal(0, budget.RemainingBytes);

    Assert.False(budget.TryWrite(1));
    Assert.Equal(10, budget.WrittenBytes);
  }

  /// <summary>Отказ при превышении на один байт без переполнения сравнения.</summary>
  [Fact]
  public void Письмо_ПревышениеНаБайт_Отказано_БезПереполнения()
  {
    var budget = new ExtractionBudget(long.MaxValue - 1, 1);

    Assert.False(budget.TryWrite(long.MaxValue));
    Assert.Equal(0, budget.WrittenBytes);
    Assert.Equal(long.MaxValue - 1, budget.RemainingBytes);
  }

  /// <summary>Атомарность отказа: частичного списания при отказе нет (всё или ничего).</summary>
  [Fact]
  public void Письмо_Отмена_НеВызываетЧастичноеСписание()
  {
    var budget = new ExtractionBudget(10, 5);

    Assert.True(budget.TryWrite(6));
    Assert.False(budget.TryWrite(5));
    Assert.Equal(6, budget.WrittenBytes);
    Assert.Equal(4, budget.RemainingBytes);
  }

  /// <summary>Отрицательный объём отклоняется и не меняет состояние.</summary>
  [Fact]
  public void Письмо_ОтрицательныйОбъём_Отказано_Состояние_НеМеняется()
  {
    var budget = new ExtractionBudget(10, 1);

    Assert.False(budget.TryWrite(-1));
    Assert.False(budget.TryWrite(long.MinValue));
    Assert.Equal(0, budget.WrittenBytes);
  }

  /// <summary>Аккумулирование нескольких статей расхода до предела.</summary>
  [Fact]
  public void Письмо_Накопление_Статей_ДоПредела()
  {
    var budget = new ExtractionBudget(10, 3);

    Assert.True(budget.TryWrite(3));
    Assert.True(budget.TryWrite(4));
    Assert.True(budget.TryWrite(3));
    Assert.Equal(10, budget.WrittenBytes);
    Assert.Equal(0, budget.RemainingBytes);

    Assert.False(budget.TryWrite(1));
  }

  /// <summary>Отсутствие отрицательных остатков на пределе типа.</summary>
  [Fact]
  public void Письмо_НаПределе_Типа_Остаток_НеОтрицателен()
  {
    var budget = new ExtractionBudget(long.MaxValue, 1);

    Assert.True(budget.TryWrite(long.MaxValue));
    Assert.Equal(long.MaxValue, budget.WrittenBytes);
    Assert.Equal(0, budget.RemainingBytes);

    Assert.False(budget.TryWrite(1));
    Assert.False(budget.RemainingBytes < 0);
  }

  /// <summary>Предел числа элементов независим от байтового бюджета.</summary>
  [Fact]
  public void Начало_Элемента_Отказ_НаПределе_Элементов()
  {
    var budget = new ExtractionBudget(1000, 2);

    Assert.True(budget.TryBeginEntry());
    Assert.True(budget.TryBeginEntry());
    Assert.False(budget.TryBeginEntry());
    Assert.Equal(2, budget.EntriesBegun);

    // Бюджетный счётчик при этом не исчерпан — счётчики независимы.
    Assert.Equal(1000, budget.RemainingBytes);
  }

  /// <summary>Fail-closed на отрицательных пределах: ничего выдать нельзя, но ноль допустим.</summary>
  [Fact]
  public void Конструктор_ОтрицательныеПределы_FailClosed()
  {
    var budget = new ExtractionBudget(-1, -1);

    Assert.Equal(0, budget.MaxBytes);
    Assert.Equal(0, budget.MaxEntries);
    Assert.Equal(0, budget.RemainingBytes);
    Assert.False(budget.RemainingBytes < 0);

    Assert.False(budget.TryWrite(1));
    Assert.False(budget.TryBeginEntry());

    // Нулевая выдача не нарушает лимит.
    Assert.True(budget.TryWrite(0));
  }
}
