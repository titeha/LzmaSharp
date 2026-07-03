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
  public string DisplaySize => IsDirectory ? string.Empty : ByteSizeFormat.Format(Size);

  /// <summary>Тип элемента для колонки.</summary>
  public string Kind => IsDirectory ? "папка" : "файл";

  /// <summary>Значок элемента (папка/файл).</summary>
  public string Icon => IsDirectory ? "📁" : "📄";
}
