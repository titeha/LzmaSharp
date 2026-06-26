using System.IO;

namespace Lzma.Ui.Services;

/// <summary>
/// Чистые правила построения имён записей архива из путей файловой системы.
/// Вынесено за швы ввода-вывода, чтобы тестировать без диска.
/// </summary>
internal static class ArchiveEntryNaming
{
  /// <summary>
  /// Строит имя записи для файла внутри выбранной папки: имя самой папки как верхний
  /// сегмент + относительный путь файла, разделитель — всегда '/'.
  /// </summary>
  /// <example>
  /// root = <c>C:\data\docs</c>, file = <c>C:\data\docs\sub\a.txt</c> → <c>docs/sub/a.txt</c>.
  /// </example>
  public static string ForFileUnderFolder(string rootFolderPath, string fileFullPath)
  {
    string root = rootFolderPath.TrimEnd('/', '\\');
    string rootName = Path.GetFileName(root);
    string relative = Path.GetRelativePath(root, fileFullPath).Replace('\\', '/');

    return string.IsNullOrEmpty(rootName)
        ? relative
        : rootName + "/" + relative;
  }
}
