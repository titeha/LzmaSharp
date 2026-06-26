using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="ISaveFilePicker"/> через диалог «Сохранить как…» Avalonia
/// (StorageProvider). Возвращает локальный путь выбранного файла.
/// </summary>
public sealed class AvaloniaSaveFilePicker(TopLevel topLevel) : ISaveFilePicker
{
  private readonly TopLevel _topLevel = topLevel;

  public async Task<string?> PickSavePathAsync(string suggestedFileName)
  {
    IStorageFile? file = await _topLevel.StorageProvider.SaveFilePickerAsync(
        new FilePickerSaveOptions
        {
          Title = "Сохранить архив как",
          SuggestedFileName = suggestedFileName,
          DefaultExtension = "7z",
          FileTypeChoices =
          [
            new FilePickerFileType("Архив 7z") { Patterns = ["*.7z"] },
          ],
        });

    if (file is null)
      return null;

    string? path = file.TryGetLocalPath();
    return string.IsNullOrEmpty(path) ? null : path;
  }
}
