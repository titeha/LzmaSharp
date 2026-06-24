namespace Lzma.Ui.Models;

/// <summary>
/// Строка списка содержимого архива для отображения в UI.
/// </summary>
public sealed class ArchiveItem
{
  /// <summary>Имя (путь внутри архива).</summary>
  public required string Name { get; init; }

  /// <summary>Признак каталога.</summary>
  public required bool IsDirectory { get; init; }

  /// <summary>Размер в байтах (для файлов; для каталогов — 0).</summary>
  public required long Size { get; init; }

  /// <summary>Человекочитаемый размер; для каталога — пусто.</summary>
  public string DisplaySize => IsDirectory ? string.Empty : FormatSize(Size);

  /// <summary>Тип элемента для колонки.</summary>
  public string Kind => IsDirectory ? "папка" : "файл";

  /// <summary>Значок элемента (папка/файл).</summary>
  public string Icon => IsDirectory ? "📁" : "📄";

  private static string FormatSize(long bytes)
  {
    string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
    double value = bytes;
    int unit = 0;

    while (value >= 1024 && unit < units.Length - 1)
    {
      value /= 1024;
      unit++;
    }

    return unit == 0
        ? $"{bytes} {units[unit]}"
        : $"{value:0.#} {units[unit]}";
  }
}
