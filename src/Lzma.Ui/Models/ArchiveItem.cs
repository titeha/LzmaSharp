using MvvmUtilites;

namespace Lzma.Ui.Models;

/// <summary>
/// Строка списка для отображения в UI: элемент содержимого архива ИЛИ элемент файловой системы
/// (в режиме браузера). Для элементов ФС задан <see cref="FullPath"/>.
/// </summary>
public sealed class ArchiveItem : ObservableObject
{
  private bool _isSelected;

  /// <summary>Имя (путь внутри архива либо имя файла/папки в ФС).</summary>
  public required string Name { get; init; }

  /// <summary>Признак каталога.</summary>
  public required bool IsDirectory { get; init; }

  /// <summary>Размер в байтах (для файлов; для каталогов — 0).</summary>
  public required long Size { get; init; }

  /// <summary>
  /// Полный путь в файловой системе — задан только в режиме браузера ФС; для элементов
  /// содержимого архива <see langword="null"/>.
  /// </summary>
  public string? FullPath { get; init; }

  /// <summary>Отмечен ли элемент галочкой (мультивыбор в браузере ФС).</summary>
  public bool IsSelected
  {
    get => _isSelected;
    set => Set(ref _isSelected, value);
  }

  /// <summary>Человекочитаемый размер; для каталога — пусто.</summary>
  public string DisplaySize => IsDirectory ? string.Empty : ByteSizeFormat.Format(Size);

  /// <summary>Является ли элемент файлом-архивом (по расширению) — для значка/действий.</summary>
  public bool IsArchiveFile => !IsDirectory && IsArchiveName(Name);

  /// <summary>Обычный файл (не каталог и не архив) — для выбора векторного значка.</summary>
  public bool IsPlainFile => !IsDirectory && !IsArchiveFile;

  /// <summary>Тип элемента для колонки.</summary>
  public string Kind => IsDirectory ? "папка" : IsArchiveFile ? "архив" : "файл";

  /// <summary>Распознаёт имя архива по расширению (.7z/.zip и первый том .7z.001).</summary>
  public static bool IsArchiveName(string name)
  {
    if (name.EndsWith(".7z", System.StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    // Первый том многотомного 7z: name.7z.001
    return name.EndsWith(".7z.001", System.StringComparison.OrdinalIgnoreCase);
  }
}
