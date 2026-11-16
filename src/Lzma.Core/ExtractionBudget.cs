namespace Lzma.Core;

/// <summary>
/// Бюджет операции извлечения (SEC-004): учёт фактически выданных байтов и числа элементов
/// против пределов, заданных при создании.
/// </summary>
/// <remarks>
/// <para>Пределы выводятся вызывающим кодом из ЗАЯВЛЕННЫХ в архиве значений (сумма заявленных
/// распакованных размеров извлекаемых элементов и их число), поэтому легитимный архив бюджетом не
/// отклоняется, а расхождение заявленного и фактического останавливает выдачу до превышения.</para>
/// <para>Тип не работает с файловой системой и потоками — только счётчики, поэтому проверяется
/// изолированными тестами без фиксации на диске. Lifetime — одна операция извлечения; тип не
/// потокобезопасен (как <see cref="StagedExtractionJournal"/>).</para>
/// <para>Все величины неотрицательны: отрицательный предел при создании приводит к нулю
/// (fail-closed — ничего выдать нельзя), остаток никогда не уходит ниже нуля, а учёт байтов
/// выполняется по принципу «всё или ничего», поэтому переполнение <see cref="long"/> исключено.</para>
/// </remarks>
internal sealed class ExtractionBudget
{
  private readonly long _maxBytes;
  private readonly long _maxEntries;
  private long _writtenBytes;
  private long _entriesBegun;

  /// <param name="maxBytes">Предел суммарного числа выданных байтов; отрицательный → 0.</param>
  /// <param name="maxEntries">Предел числа элементов; отрицательный → 0.</param>
  public ExtractionBudget(long maxBytes, long maxEntries)
  {
    _maxBytes = maxBytes < 0 ? 0 : maxBytes;
    _maxEntries = maxEntries < 0 ? 0 : maxEntries;
  }

  /// <summary>Предел суммарного числа выданных байтов (неотрицателен).</summary>
  public long MaxBytes => _maxBytes;

  /// <summary>Предел числа элементов (неотрицателен).</summary>
  public long MaxEntries => _maxEntries;

  /// <summary>Сколько байтов фактически учтено; не превышает <see cref="MaxBytes"/>.</summary>
  public long WrittenBytes => _writtenBytes;

  /// <summary>Сколько элементов начато; не превышает <see cref="MaxEntries"/>.</summary>
  public long EntriesBegun => _entriesBegun;

  /// <summary>Сколько байт ещё можно выдать; никогда не отрицательно.</summary>
  public long RemainingBytes => _maxBytes - _writtenBytes;

  /// <summary>
  /// Отмечает начало очередного элемента. Возвращает <see langword="false"/>, если предел числа
  /// элементов исчерпан; при отказе счётчик не увеличивается.
  /// </summary>
  public bool TryBeginEntry()
  {
    if (_entriesBegun >= _maxEntries)
      return false;

    _entriesBegun++;
    return true;
  }

  /// <summary>
  /// Учитывает <paramref name="count"/> выданных байт. Возвращает <see langword="false"/>, если
  /// <paramref name="count"/> отрицателен либо превысил бы <see cref="RemainingBytes"/>; при отказе
  /// состояние не меняется (учёт «всё или ничего», частичного списания нет).
  /// </summary>
  public bool TryWrite(long count)
  {
    if (count < 0 || count > RemainingBytes)
      return false;

    _writtenBytes += count;
    return true;
  }
}
