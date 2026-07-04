using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="ISourceFilesPicker"/> через диалог множественного выбора файлов
/// Avalonia (StorageProvider). Содержимое выбранных файлов читается в память.
/// </summary>
public sealed class AvaloniaSourceFilesPicker(TopLevel topLevel) : ISourceFilesPicker
{
  private readonly TopLevel _topLevel = topLevel;

  public async Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default)
  {
    IReadOnlyList<IStorageFile> files = await _topLevel.StorageProvider.OpenFilePickerAsync(
        new FilePickerOpenOptions
        {
          Title = "Выберите файлы для архива",
          AllowMultiple = true,
        });

    if (files.Count == 0)
      return null;

    var result = new List<PickedFile>(files.Count);
    long bytesRead = 0;

    foreach (IStorageFile file in files)
    {
      token.ThrowIfCancellationRequested();

      await using Stream stream = await file.OpenReadAsync();
      using var buffer = new MemoryStream();
      await stream.CopyToAsync(buffer, token);

      byte[] bytes = buffer.ToArray();
      result.Add(new PickedFile(file.Name, bytes));

      bytesRead += bytes.LongLength;
      progress?.Report(new ScanProgress(result.Count, bytesRead));
    }

    return result;
  }
}
