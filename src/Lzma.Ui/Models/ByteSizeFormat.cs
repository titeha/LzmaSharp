namespace Lzma.Ui.Models;

/// <summary>
/// Человекочитаемое форматирование размера в байтах (Б/КБ/МБ/ГБ/ТБ). Общий хелпер,
/// чтобы не дублировать логику между списком содержимого и счётчиком сканирования.
/// </summary>
public static class ByteSizeFormat
{
  private static readonly string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];

  /// <summary>Форматирует размер: целые байты для &lt; 1 КБ, иначе одна десятичная и единица.</summary>
  public static string Format(long bytes)
  {
    double value = bytes;
    int unit = 0;

    while (value >= 1024 && unit < Units.Length - 1)
    {
      value /= 1024;
      unit++;
    }

    return unit == 0
        ? $"{bytes} {Units[unit]}"
        : $"{value:0.#} {Units[unit]}";
  }
}
