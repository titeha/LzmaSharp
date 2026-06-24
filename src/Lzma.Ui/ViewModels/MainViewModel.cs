using System.Collections.ObjectModel;
using System.Linq;

using Lzma.Core.SevenZip;
using Lzma.Ui.Models;
using Lzma.Ui.Services;

using MvvmUtilites;

namespace Lzma.Ui.ViewModels;

/// <summary>
/// Главная модель представления окна архиватора.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
  /// <summary>Базовый заголовок окна, когда архив не открыт.</summary>
  public const string DefaultTitle = "LzmaSharp — архиватор";

  private readonly IArchivePicker _picker;

  private string _title = DefaultTitle;
  private string? _statusMessage;
  private bool _hasArchive;

  public MainViewModel(IArchivePicker picker)
  {
    _picker = picker;
    OpenCommand = new AsyncRelayCommand(OpenAsync);
  }

  /// <summary>Заголовок окна: базовый либо «имя_архива — LzmaSharp» при открытом архиве.</summary>
  public string Title
  {
    get => _title;
    set => Set(ref _title, value);
  }

  /// <summary>Статусное сообщение (ошибка/пустое состояние); <see langword="null"/> — скрыто.</summary>
  public string? StatusMessage
  {
    get => _statusMessage;
    set => Set(ref _statusMessage, value);
  }

  /// <summary>Открыт ли архив (есть содержимое для показа).</summary>
  public bool HasArchive
  {
    get => _hasArchive;
    set => Set(ref _hasArchive, value);
  }

  /// <summary>Содержимое открытого архива.</summary>
  public ObservableCollection<ArchiveItem> Items { get; } = [];

  /// <summary>Команда «Открыть архив…».</summary>
  public AsyncRelayCommand OpenCommand { get; }

  private async Task OpenAsync()
  {
    PickedArchive? picked = await _picker.PickAsync();

    if (picked is null)
      return; // выбор отменён — состояние не трогаем

    // Декодирование — CPU-работа, уводим с UI-потока, чтобы окно не подвисало.
    (SevenZipArchiveDecodeResult result, SevenZipDecodedEntry[] entries) = await Task.Run(() =>
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(picked.Bytes, out SevenZipDecodedEntry[] e);
      return (r, e);
    });

    ApplyResult(picked.Name, result, entries);
  }

  // Чистая логика применения результата — без UI/IO, удобно тестировать.
  internal void ApplyResult(string archiveName, SevenZipArchiveDecodeResult result, SevenZipDecodedEntry[] entries)
  {
    Items.Clear();

    switch (result)
    {
      case SevenZipArchiveDecodeResult.Ok:
        foreach (SevenZipDecodedEntry entry in entries
                     .OrderByDescending(e => e.IsDirectory)
                     .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
          Items.Add(new ArchiveItem
          {
            Name = entry.Name,
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : entry.Bytes.LongLength,
          });
        }

        HasArchive = true;
        Title = $"{archiveName} — LzmaSharp";
        StatusMessage = entries.Length == 0 ? "Архив пуст." : null;
        break;

      case SevenZipArchiveDecodeResult.NotSupported:
        HasArchive = false;
        Title = DefaultTitle;
        StatusMessage = "Не удалось открыть: возможно, архив зашифрован (ввод пароля будет добавлен) "
                      + "или используется неподдерживаемая возможность.";
        break;

      default: // InvalidData и прочее
        HasArchive = false;
        Title = DefaultTitle;
        StatusMessage = "Не удалось открыть: файл повреждён или не является поддерживаемым 7z-архивом.";
        break;
    }
  }
}
