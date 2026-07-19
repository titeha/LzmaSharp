using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

using MvvmUtilites;

namespace Lzma.Ui.Models;

/// <summary>
/// Узел раскрываемого дерева браузера (папка/файл). Дети догружаются ЛЕНИВО при первом раскрытии
/// (<see cref="IsExpanded"/>) через переданный загрузчик — файловая система огромна, всё сразу не
/// строим. У нераскрытой папки держится узел-заглушка, чтобы показать треугольник раскрытия.
/// </summary>
public sealed class TreeNodeItem : ObservableObject
{
  private readonly Func<TreeNodeItem, IReadOnlyList<TreeNodeItem>>? _loadChildren;
  private bool _isExpanded;
  private bool _isSelected;
  private bool _loaded;

  /// <param name="loadChildren">Загрузчик детей (вызывается один раз при раскрытии папки).</param>
  public TreeNodeItem(Func<TreeNodeItem, IReadOnlyList<TreeNodeItem>>? loadChildren = null)
  {
    _loadChildren = loadChildren;
  }

  /// <summary>Отображаемое имя (папки/файла или метка корня).</summary>
  public required string Name { get; init; }

  /// <summary>Признак каталога (папка/корень).</summary>
  public required bool IsDirectory { get; init; }

  /// <summary>Размер в байтах (для файлов; для папок — 0).</summary>
  public required long Size { get; init; }

  /// <summary>Полный путь в ФС (для навигации/выбора); может быть <see langword="null"/> для заглушки.</summary>
  public string? FullPath { get; init; }

  /// <summary>Дети узла (у нераскрытой папки — один узел-заглушка; после раскрытия — реальные).</summary>
  public ObservableCollection<TreeNodeItem> Children { get; } = [];

  /// <summary>Раскрыт ли узел. При первом раскрытии папки лениво догружает детей.</summary>
  public bool IsExpanded
  {
    get => _isExpanded;
    set
    {
      if (Set(ref _isExpanded, value) && value)
        EnsureLoaded();
    }
  }

  /// <summary>Отмечен ли узел галочкой (мультивыбор по всему дереву).</summary>
  public bool IsSelected
  {
    get => _isSelected;
    set => Set(ref _isSelected, value);
  }

  /// <summary>Человекочитаемый размер; для папки — пусто.</summary>
  public string DisplaySize => IsDirectory ? string.Empty : ByteSizeFormat.Format(Size);

  /// <summary>Файл-архив (по расширению) — для значка/действий.</summary>
  public bool IsArchiveFile => !IsDirectory && ArchiveItem.IsArchiveName(Name);

  /// <summary>Обычный файл (не папка и не архив) — для выбора значка.</summary>
  public bool IsPlainFile => !IsDirectory && !IsArchiveFile;

  /// <summary>Тип элемента для колонки.</summary>
  public string Kind => IsDirectory ? "папка" : IsArchiveFile ? "архив" : "файл";

  /// <summary>Добавляет узел-заглушку папке, чтобы показать треугольник раскрытия до догрузки.</summary>
  public void AddLoadingPlaceholder()
  {
    if (IsDirectory)
      Children.Add(new TreeNodeItem { Name = string.Empty, IsDirectory = false, Size = 0 });
  }

  /// <summary>Догружает детей один раз (замена заглушки реальными узлами). Идемпотентно.</summary>
  public void EnsureLoaded()
  {
    if (_loaded || !IsDirectory || _loadChildren is null)
      return;

    _loaded = true;
    Children.Clear();
    foreach (TreeNodeItem child in _loadChildren(this))
      Children.Add(child);
  }

  /// <summary>Загружены ли уже реальные дети (для тестов/логики).</summary>
  public bool IsLoaded => _loaded;
}
