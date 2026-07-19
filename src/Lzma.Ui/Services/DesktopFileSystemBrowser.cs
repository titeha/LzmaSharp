using System;
using System.Collections.Generic;
using System.IO;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="IFileSystemBrowser"/> поверх <c>System.IO</c> для десктопа.
/// Недоступные элементы (нет прав, устройство не готово) молча пропускаются.
/// </summary>
public sealed class DesktopFileSystemBrowser : IFileSystemBrowser
{
  /// <inheritdoc />
  public IReadOnlyList<FileSystemEntry> ListRoots()
  {
    var roots = new List<FileSystemEntry>();

    foreach (DriveInfo drive in DriveInfo.GetDrives())
    {
      string name;
      try
      {
        if (!drive.IsReady)
          continue;

        // «C:\ (Метка)» или просто «C:\», если метки нет.
        string label = drive.VolumeLabel;
        name = string.IsNullOrEmpty(label) ? drive.Name : $"{drive.Name} ({label})";
      }
      catch (IOException)
      {
        continue;
      }
      catch (System.UnauthorizedAccessException)
      {
        continue;
      }

      roots.Add(new FileSystemEntry(name, drive.RootDirectory.FullName, IsDirectory: true, Size: 0));
    }

    return roots;
  }

  /// <inheritdoc />
  public IReadOnlyList<FileSystemEntry> ListSpecialFolders()
  {
    var list = new List<FileSystemEntry>();

    void TryAdd(string display, string? path)
    {
      if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
        list.Add(new FileSystemEntry(display, path, IsDirectory: true, Size: 0));
    }

    // Environment.GetFolderPath кросс-платформенна: Windows — известные папки, Linux/macOS — XDG/HOME
    // (несуществующие отсекаются проверкой). Загрузки в SpecialFolder нет — собираем из профиля.
    TryAdd("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
    string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (!string.IsNullOrEmpty(profile))
      TryAdd("Загрузки", Path.Combine(profile, "Downloads"));
    TryAdd("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    TryAdd("Изображения", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
    TryAdd("Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
    TryAdd("Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

    return list;
  }

  /// <inheritdoc />
  public IReadOnlyList<FileSystemEntry> ListDirectory(string fullPath)
  {
    var entries = new List<FileSystemEntry>();

    DirectoryInfo dir;
    try
    {
      dir = new DirectoryInfo(fullPath);
      if (!dir.Exists)
        return entries;
    }
    catch (System.ArgumentException)
    {
      return entries;
    }

    // Папки.
    try
    {
      foreach (DirectoryInfo sub in dir.EnumerateDirectories())
      {
        // Скрытые/системные каталоги показываем — фильтрация оставлена на будущее.
        entries.Add(new FileSystemEntry(sub.Name, sub.FullName, IsDirectory: true, Size: 0));
      }
    }
    catch (IOException) { }
    catch (System.UnauthorizedAccessException) { }

    // Файлы.
    try
    {
      foreach (FileInfo file in dir.EnumerateFiles())
      {
        long size;
        try { size = file.Length; }
        catch (IOException) { size = 0; }

        entries.Add(new FileSystemEntry(file.Name, file.FullName, IsDirectory: false, size));
      }
    }
    catch (IOException) { }
    catch (System.UnauthorizedAccessException) { }

    return entries;
  }

  /// <inheritdoc />
  public Stream OpenRead(string fullPath) => File.OpenRead(fullPath);

  /// <inheritdoc />
  public string? ResolveDirectory(string path)
  {
    if (string.IsNullOrWhiteSpace(path))
      return null;

    try
    {
      // DirectoryInfo.FullName нормализует ('C:\Foo\' → 'C:\Foo', сохраняя корень 'C:\').
      string full = new DirectoryInfo(path).FullName;
      return Directory.Exists(full) ? full : null;
    }
    catch (System.ArgumentException) { return null; }
    catch (IOException) { return null; }
    catch (System.UnauthorizedAccessException) { return null; }
    catch (System.Security.SecurityException) { return null; }
  }

  /// <inheritdoc />
  public IReadOnlyList<ArchiveSourceFile> EnumerateForArchive(IReadOnlyList<string> paths)
  {
    var result = new List<ArchiveSourceFile>();

    // Недоступные подкаталоги при обходе папки просто пропускаются.
    var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };

    foreach (string path in paths)
    {
      if (Directory.Exists(path))
      {
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(path, "*", options); }
        catch (IOException) { continue; }
        catch (System.UnauthorizedAccessException) { continue; }

        foreach (string file in files)
          result.Add(new ArchiveSourceFile(ArchiveEntryNaming.ForFileUnderFolder(path, file), file, SafeLength(file)));
      }
      else if (File.Exists(path))
      {
        result.Add(new ArchiveSourceFile(Path.GetFileName(path), path, SafeLength(path)));
      }
    }

    return result;
  }

  private static long SafeLength(string file)
  {
    try { return new FileInfo(file).Length; }
    catch (IOException) { return 0; }
    catch (System.UnauthorizedAccessException) { return 0; }
  }

  /// <inheritdoc />
  public string? GetParent(string fullPath)
  {
    try
    {
      return Directory.GetParent(fullPath)?.FullName;
    }
    catch (IOException)
    {
      return null;
    }
    catch (System.UnauthorizedAccessException)
    {
      return null;
    }
    catch (System.ArgumentException)
    {
      return null;
    }
  }
}
