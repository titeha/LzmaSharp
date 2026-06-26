using System.Collections.Generic;
using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>
/// Абстракция выбора папки-источника для упаковки. В отличие от <see cref="ISourceFilesPicker"/>
/// рекурсивно собирает все файлы внутри выбранной папки; имена записей — относительные пути
/// (верхний сегмент — имя самой папки). Отделяет ViewModel от диалога и обхода диска для тестов.
/// </summary>
public interface ISourceFolderPicker
{
  /// <summary>
  /// Просит выбрать папку и возвращает её файлы как <see cref="PickedFile"/> с относительными
  /// именами. <see langword="null"/> — выбор отменён или в папке нет файлов.
  /// </summary>
  Task<IReadOnlyList<PickedFile>?> PickFolderFilesAsync();
}
