namespace Lzma.Ui.Models;

/// <summary>
/// Один сегмент «хлебных крошек» текущего пути. Кликабелен для навигации к этому уровню
/// (в браузере ФС — по <see cref="FullPath"/>; в открытом архиве — по <see cref="Depth"/>).
/// </summary>
public sealed class PathCrumb
{
  /// <summary>Отображаемое имя сегмента (диск, папка, имя архива или «Этот компьютер»).</summary>
  public required string Name { get; init; }

  /// <summary>
  /// Полный путь каталога ФС для перехода (null — корень «Этот компьютер»).
  /// В режиме архива не используется.
  /// </summary>
  public string? FullPath { get; init; }

  /// <summary>Глубина узла в дереве архива от корня (0 — корень архива). В режиме ФС не используется.</summary>
  public int Depth { get; init; }

  /// <summary>Текущий (последний) сегмент пути — не ведёт никуда, подсвечивается как активный.</summary>
  public bool IsCurrent { get; init; }
}
