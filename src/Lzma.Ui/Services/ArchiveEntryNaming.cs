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
  /// На Windows '\' нормализуется в '/'; на POSIX '\' — легальный символ имени файла
  /// и сохраняется без замены.
  /// </summary>
  /// <example>
  /// root = <c>C:\data\docs</c>, file = <c>C:\data\docs\sub\a.txt</c> → <c>docs/sub/a.txt</c>.
  /// </example>
  public static string ForFileUnderFolder(string rootFolderPath, string fileFullPath)
  {
    string root = rootFolderPath.TrimEnd('/', '\\');
    string rootName = Path.GetFileName(root);
    string relative = Path.GetRelativePath(root, fileFullPath);

    if (OperatingSystem.IsWindows())
    {
      // На Windows '\' — разделитель пути; имя записи архива всегда использует '/'.
      // На POSIX '\' — легальный символ имени файла, поэтому замена не выполняется.
      relative = relative.Replace('\\', '/');
    }

    return string.IsNullOrEmpty(rootName)
        ? relative
        : rootName + "/" + relative;
  }
}
