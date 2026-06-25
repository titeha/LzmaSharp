using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="IFolderPicker"/> через диалог выбора папки Avalonia (StorageProvider).
/// </summary>
public sealed class AvaloniaFolderPicker(TopLevel topLevel) : IFolderPicker
{
  private readonly TopLevel _topLevel = topLevel;

  public async Task<string?> PickFolderAsync()
  {
    IReadOnlyList<IStorageFolder> folders = await _topLevel.StorageProvider.OpenFolderPickerAsync(
        new FolderPickerOpenOptions
        {
          Title = "Куда извлечь",
          AllowMultiple = false,
        });

    if (folders.Count == 0)
      return null;

    string? path = folders[0].TryGetLocalPath();
    return string.IsNullOrEmpty(path) ? null : path;
  }
}
