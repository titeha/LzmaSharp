using System.Collections.Generic;
using System.IO;
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

  public async Task<IReadOnlyList<PickedFile>?> PickFilesAsync()
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

    foreach (IStorageFile file in files)
    {
      await using Stream stream = await file.OpenReadAsync();
      using var buffer = new MemoryStream();
      await stream.CopyToAsync(buffer);

      result.Add(new PickedFile(file.Name, buffer.ToArray()));
    }

    return result;
  }
}
