using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="ISourceFolderPicker"/> через диалог выбора папки Avalonia.
/// Рекурсивно читает все файлы выбранной папки в память с относительными именами записей.
/// </summary>
public sealed class AvaloniaSourceFolderPicker(TopLevel topLevel) : ISourceFolderPicker
{
  private readonly TopLevel _topLevel = topLevel;

  public async Task<IReadOnlyList<PickedFile>?> PickFolderFilesAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default)
  {
    IReadOnlyList<IStorageFolder> folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(
        new FolderPickerOpenOptions
        {
          Title = "Выберите папку для архива",
          AllowMultiple = false,
        });

    if (folders.Count == 0)
      return null;

    string? root = folders[0].TryGetLocalPath();

    if (string.IsNullOrEmpty(root))
      return null;

    var result = new List<PickedFile>();
    long bytesRead = 0;

    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
      token.ThrowIfCancellationRequested();

      byte[] bytes = await File.ReadAllBytesAsync(file, token);
      result.Add(new PickedFile(ArchiveEntryNaming.ForFileUnderFolder(root, file), bytes));

      bytesRead += bytes.LongLength;
      progress?.Report(new ScanProgress(result.Count, bytesRead));
    }

    return result.Count == 0 ? null : result; // пустая папка — паковать нечего
  }

  public bool SupportsRefs => true;

  public async Task<IReadOnlyList<PickedFileRef>?> PickFolderFileRefsAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default)
  {
    IReadOnlyList<IStorageFolder> folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(
        new FolderPickerOpenOptions
        {
          Title = "Выберите папку для архива",
          AllowMultiple = false,
        });

    if (folders.Count == 0)
      return null;

    string? root = folders[0].TryGetLocalPath();

    if (string.IsNullOrEmpty(root))
      return null;

    var result = new List<PickedFileRef>();
    long bytesTotal = 0;

    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
      token.ThrowIfCancellationRequested();

      long length = new FileInfo(file).Length;
      string capturedPath = file;
      result.Add(new PickedFileRef(
          ArchiveEntryNaming.ForFileUnderFolder(root, file), length, () => File.OpenRead(capturedPath)));

      bytesTotal += length;
      progress?.Report(new ScanProgress(result.Count, bytesTotal));
    }

    return result.Count == 0 ? null : result;
  }
}
