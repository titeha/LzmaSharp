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
