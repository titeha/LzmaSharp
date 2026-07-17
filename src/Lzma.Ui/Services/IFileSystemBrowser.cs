using System.Collections.Generic;

namespace Lzma.Ui.Services;

/// <summary>
/// Элемент файловой системы для браузера главного окна.
/// </summary>
/// <param name="Name">Отображаемое имя (имя файла/папки или метка корня, напр. «C:\»).</param>
/// <param name="FullPath">Полный путь для навигации/операций.</param>
/// <param name="IsDirectory">Признак каталога (или корня-диска).</param>
/// <param name="Size">Размер файла в байтах; для каталогов — 0.</param>
public readonly record struct FileSystemEntry(string Name, string FullPath, bool IsDirectory, long Size);

/// <summary>
/// Исходный файл для помещения в архив: имя записи в архиве (с относительным путём), полный путь
/// на диске и длина.
/// </summary>
/// <param name="EntryName">Имя записи в архиве (разделитель '/'; для файла из папки — «папка/…»).</param>
/// <param name="FullPath">Полный путь файла на диске.</param>
/// <param name="Length">Длина файла в байтах.</param>
public readonly record struct ArchiveSourceFile(string EntryName, string FullPath, long Length);

/// <summary>
/// Шов доступа к файловой системе для браузера. Изолирует <see cref="ViewModels.MainViewModel"/>
/// от прямых вызовов <c>System.IO</c>, чтобы модель представления осталась платформо-независимой
/// (в т.ч. для будущего переноса на Android) и тестируемой на фейке.
/// </summary>
public interface IFileSystemBrowser
{
  /// <summary>Корни файловой системы (диски / «Этот компьютер»).</summary>
  IReadOnlyList<FileSystemEntry> ListRoots();

  /// <summary>Содержимое каталога (папки и файлы); недоступные элементы пропускаются.</summary>
  IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath);

  /// <summary>
  /// Родительский каталог для <paramref name="fullPath"/>, либо <see langword="null"/>, если это
  /// корень диска (тогда навигация вверх ведёт к списку корней).
  /// </summary>
  string? GetParent(string fullPath);

  /// <summary>Открывает файл для чтения (например, чтобы открыть архив из браузера).</summary>
  System.IO.Stream OpenRead(string fullPath);

  /// <summary>
  /// Разворачивает выбранные пути (файлы и папки) в набор исходных файлов для архива: файл берётся
  /// как есть (имя = имя файла), папка обходится рекурсивно (имена — «папка/относительный/путь»).
  /// </summary>
  IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths);
}
