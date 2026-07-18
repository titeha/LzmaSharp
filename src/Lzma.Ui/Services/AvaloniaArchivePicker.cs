using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="IArchivePicker"/> через файловый диалог Avalonia (StorageProvider).
/// </summary>
public sealed class AvaloniaArchivePicker(TopLevel topLevel) : IArchivePicker
{
  private readonly TopLevel _topLevel = topLevel;

  public async Task<PickedArchive?> PickAsync()
  {
    IReadOnlyList<IStorageFile> files = await _topLevel.StorageProvider.OpenFilePickerAsync(
        new FilePickerOpenOptions
        {
          Title = "Открыть архив",
          AllowMultiple = false,
          FileTypeFilter =
          [
            new FilePickerFileType("Архивы") { Patterns = ["*.7z", "*.zip"] },
            new FilePickerFileType("Архивы 7z") { Patterns = ["*.7z"] },
            new FilePickerFileType("ZIP-архивы") { Patterns = ["*.zip"] },
            FilePickerFileTypes.All,
          ],
        });

    if (files.Count == 0)
      return null;

    IStorageFile file = files[0];

    await using Stream stream = await file.OpenReadAsync();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);

    return new PickedArchive(file.Name, buffer.ToArray());
  }

  public bool SupportsUnifiedOpen => true;

  public async Task<PickedOpenTarget?> PickForOpenAsync()
  {
    IReadOnlyList<IStorageFile> files = await _topLevel.StorageProvider.OpenFilePickerAsync(
        new FilePickerOpenOptions
        {
          Title = "Открыть архив",
          AllowMultiple = false,
          FileTypeFilter =
          [
            new FilePickerFileType("Архивы") { Patterns = ["*.7z", "*.zip"] },
            new FilePickerFileType("Архивы 7z") { Patterns = ["*.7z"] },
            new FilePickerFileType("ZIP-архивы") { Patterns = ["*.zip"] },
            FilePickerFileTypes.All,
          ],
        });

    if (files.Count == 0)
      return null;

    IStorageFile file = files[0];

    // Локальный файл — отдаём путь и размер (в память НЕ читаем: авто-выбор по размеру во VM).
    if (file.TryGetLocalPath() is { } localPath)
    {
      long length = 0;
      try { length = new FileInfo(localPath).Length; }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }

      return new PickedOpenTarget(file.Name, length, localPath, Bytes: null);
    }

    // Нет локального пути (облако/виртуальный источник) — читаем поток в память.
    await using Stream stream = await file.OpenReadAsync();
    using var buffer = new MemoryStream();
    await stream.CopyToAsync(buffer);
    byte[] bytes = buffer.ToArray();

    return new PickedOpenTarget(file.Name, bytes.Length, LocalPath: null, bytes);
  }

  public async Task<string?> PickArchivePathAsync()
  {
    IReadOnlyList<IStorageFile> files = await _topLevel.StorageProvider.OpenFilePickerAsync(
        new FilePickerOpenOptions
        {
          Title = "Архив с диска (7z / ZIP)",
          AllowMultiple = false,
          FileTypeFilter =
          [
            new FilePickerFileType("Архивы") { Patterns = ["*.7z", "*.zip"] },
            new FilePickerFileType("Архивы 7z") { Patterns = ["*.7z"] },
            new FilePickerFileType("ZIP-архивы") { Patterns = ["*.zip"] },
            FilePickerFileTypes.All,
          ],
        });

    if (files.Count == 0)
      return null;

    // Только локальный путь — файл НЕ читаем в память (поддержка архивов > 2 ГиБ).
    return files[0].TryGetLocalPath();
  }
}
