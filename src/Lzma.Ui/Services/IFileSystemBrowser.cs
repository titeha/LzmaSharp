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
}
