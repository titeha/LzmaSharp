using System.Collections.Generic;
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
  private readonly IPasswordPrompt _passwordPrompt;
  private readonly IFolderPicker _folderPicker;
  private readonly IArchiveService _archiveService;
  private readonly ISourceFilesPicker? _sourceFilesPicker;
  private readonly ISaveFilePicker? _saveFilePicker;

  // Байты и пароль успешно открытого архива — нужны для извлечения без повторного открытия.
  private byte[]? _archiveBytes;
  private string? _archivePassword;

  // Узел виртуального дерева содержимого архива.
  private sealed class Node(string name, bool isDirectory, Node? parent)
  {
    public string Name { get; } = name;
    public bool IsDirectory { get; } = isDirectory;
    public long Size { get; set; }
    public Node? Parent { get; } = parent;
    public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
  }

  private Node _root = new(string.Empty, isDirectory: true, parent: null);
  private Node _current;

  private string _title = DefaultTitle;
  private string? _statusMessage;
  private bool _hasArchive;
  private string _currentPath = string.Empty;
  private bool _canGoUp;
  private bool _isBusy;

  public MainViewModel(IArchivePicker picker, IPasswordPrompt passwordPrompt, IFolderPicker folderPicker)
      : this(picker, passwordPrompt, folderPicker, new LzmaArchiveService(), sourceFilesPicker: null, saveFilePicker: null)
  {
  }

  public MainViewModel(
      IArchivePicker picker,
      IPasswordPrompt passwordPrompt,
      IFolderPicker folderPicker,
      IArchiveService archiveService)
      : this(picker, passwordPrompt, folderPicker, archiveService, sourceFilesPicker: null, saveFilePicker: null)
  {
  }

  public MainViewModel(
      IArchivePicker picker,
      IPasswordPrompt passwordPrompt,
      IFolderPicker folderPicker,
      IArchiveService archiveService,
      ISourceFilesPicker? sourceFilesPicker,
      ISaveFilePicker? saveFilePicker)
  {
    _picker = picker;
    _passwordPrompt = passwordPrompt;
    _folderPicker = folderPicker;
    _archiveService = archiveService;
    _sourceFilesPicker = sourceFilesPicker;
    _saveFilePicker = saveFilePicker;
    _current = _root;
    OpenCommand = new AsyncRelayCommand(OpenAsync);
    NavigateUpCommand = new RelayCommand(NavigateUp, () => CanGoUp, this);
    ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync, () => HasArchive && !IsBusy, this);
    CreateCommand = new AsyncRelayCommand(CreateAsync, () => CanCreate && !IsBusy, this);
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

  /// <summary>Текущий путь внутри архива (пусто = корень).</summary>
  public string CurrentPath
  {
    get => _currentPath;
    set => Set(ref _currentPath, value);
  }

  /// <summary>Можно ли подняться на уровень вверх.</summary>
  public bool CanGoUp
  {
    get => _canGoUp;
    set => Set(ref _canGoUp, value);
  }

  /// <summary>Идёт длительная операция (извлечение) — UI занят.</summary>
  public bool IsBusy
  {
    get => _isBusy;
    set => Set(ref _isBusy, value);
  }

  /// <summary>Содержимое текущей папки архива.</summary>
  public ObservableCollection<ArchiveItem> Items { get; } = [];

  /// <summary>Команда «Открыть архив…».</summary>
  public AsyncRelayCommand OpenCommand { get; }

  /// <summary>Команда «Вверх» (на уровень выше по дереву архива).</summary>
  public RelayCommand NavigateUpCommand { get; }

  /// <summary>Команда «Извлечь всё» — распаковать содержимое архива в выбранную папку.</summary>
  public AsyncRelayCommand ExtractAllCommand { get; }

  /// <summary>Команда «Создать архив…» — упаковать выбранные файлы в новый 7z-архив.</summary>
  public AsyncRelayCommand CreateCommand { get; }

  /// <summary>Доступные методы сжатия для создания архива (с дружелюбными именами для UI).</summary>
  public IReadOnlyList<CompressionMethodOption> CompressionMethods { get; } =
  [
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Lzma2),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Ppmd),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Auto),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Copy),
  ];

  private SevenZipWriterCompressionMethod _selectedCompressionMethod = SevenZipWriterCompressionMethod.Lzma2;

  /// <summary>Выбранный метод сжатия для создаваемого архива.</summary>
  public SevenZipWriterCompressionMethod SelectedCompressionMethod
  {
    get => _selectedCompressionMethod;
    set => Set(ref _selectedCompressionMethod, value);
  }

  /// <summary>Доступна ли функция создания архива (внедрены ли соответствующие пикеры).</summary>
  public bool CanCreate => _sourceFilesPicker is not null && _saveFilePicker is not null;

  /// <summary>Войти в элемент: для папки — перейти внутрь; файлы пока игнорируются.</summary>
  public void NavigateInto(ArchiveItem item)
  {
    if (item is null || !item.IsDirectory)
      return;

    if (_current.Children.TryGetValue(item.Name, out Node? child) && child.IsDirectory)
    {
      _current = child;
      RefreshView();
    }
  }

  /// <summary>Подняться на уровень вверх по дереву архива.</summary>
  public void NavigateUp()
  {
    if (_current.Parent is { } parent)
    {
      _current = parent;
      RefreshView();
    }
  }

  private async Task OpenAsync()
  {
    PickedArchive? picked = await _picker.PickAsync();

    if (picked is null)
      return; // выбор отменён — состояние не трогаем

    // Первая попытка — без пароля.
    (SevenZipArchiveDecodeResult result, SevenZipDecodedEntry[] entries) = await _archiveService.OpenAsync(picked.Bytes, password: null);

    if (result == SevenZipArchiveDecodeResult.Ok)
    {
      ApplyResult(picked.Name, result, entries);
      StoreOpenedArchive(picked.Bytes, password: null);
      return;
    }

    if (result != SevenZipArchiveDecodeResult.NotSupported)
    {
      // InvalidData без пароля — повреждён/не архив (пароль не спрашиваем).
      ApplyResult(picked.Name, result, entries);
      await AppendDiagnosticsAsync(picked.Bytes, password: null);
      return;
    }

    // NotSupported без пароля — возможно, архив зашифрован. Спрашиваем пароль (с повтором).
    bool previousAttemptFailed = false;

    while (true)
    {
      string? password = await _passwordPrompt.RequestAsync(picked.Name, previousAttemptFailed);

      if (password is null)
      {
        ShowPasswordCancelled();
        return; // пользователь отменил ввод пароля
      }

      (result, entries) = await _archiveService.OpenAsync(picked.Bytes, password);

      if (result == SevenZipArchiveDecodeResult.Ok)
      {
        ApplyResult(picked.Name, result, entries);
        StoreOpenedArchive(picked.Bytes, password);
        return;
      }

      if (result == SevenZipArchiveDecodeResult.InvalidData)
      {
        // Неверный пароль (несовпадение CRC) — предлагаем ввести заново.
        previousAttemptFailed = true;
        continue;
      }

      // NotSupported даже с паролем — неподдерживаемая возможность, повтор не поможет.
      ApplyResult(picked.Name, result, entries);
      await AppendDiagnosticsAsync(picked.Bytes, password);
      return;
    }
  }

  // Дополняет сообщение об ошибке списком методов архива (что именно не поддержано).
  private async Task AppendDiagnosticsAsync(byte[] bytes, string? password)
  {
    string description = await _archiveService.DescribeMethodsAsync(bytes, password);

    if (!string.IsNullOrEmpty(description))
      StatusMessage += $"  Методы в архиве: {description}.";
  }

  private void StoreOpenedArchive(byte[] bytes, string? password)
  {
    _archiveBytes = bytes;
    _archivePassword = password;
  }

  // Извлечение содержимого открытого архива в выбранную папку.
  private async Task ExtractAllAsync()
  {
    if (_archiveBytes is null)
      return;

    string? destination = await _folderPicker.PickFolderAsync();

    if (destination is null)
      return; // выбор папки отменён

    byte[] bytes = _archiveBytes;
    string? password = _archivePassword;

    IsBusy = true;

    try
    {
      SevenZipArchiveDecodeResult result = await _archiveService.ExtractAllAsync(bytes, password, destination);

      StatusMessage = result switch
      {
        SevenZipArchiveDecodeResult.Ok => $"Извлечено в: {destination}",
        SevenZipArchiveDecodeResult.NotSupported => "Извлечение не поддерживается для этого архива.",
        _ => "Не удалось извлечь: ошибка данных или файл уже существует.",
      };
    }
    finally
    {
      IsBusy = false;
    }
  }

  // Создание архива: выбор файлов → выбор пути → сборка ядром → запись на диск.
  private async Task CreateAsync()
  {
    if (_sourceFilesPicker is null || _saveFilePicker is null)
      return;

    IReadOnlyList<PickedFile>? files = await _sourceFilesPicker.PickFilesAsync();

    if (files is null || files.Count == 0)
      return; // выбор отменён или ничего не выбрано

    string? path = await _saveFilePicker.PickSavePathAsync("archive.7z");

    if (path is null)
      return; // выбор пути отменён

    IsBusy = true;

    try
    {
      var entries = new List<SevenZipArchiveWriterEntry>(files.Count);

      foreach (PickedFile file in files)
        entries.Add(new SevenZipArchiveWriterEntry(file.Name, file.Bytes));

      ArchiveCreateOutcome created = await _archiveService.CreateArchiveAsync(entries, SelectedCompressionMethod);

      if (created.Result != SevenZipArchiveWriteResult.Ok)
      {
        StatusMessage = created.Result == SevenZipArchiveWriteResult.NotSupported
            ? "Создание архива с такими параметрами не поддерживается."
            : "Не удалось создать архив: некорректный набор файлов (например, повторяющиеся имена).";
        return;
      }

      bool wrote = await _archiveService.WriteArchiveAsync(created.Archive, path);

      StatusMessage = wrote
          ? $"Создан архив: {path}"
          : "Архив собран, но записать на диск не удалось (нет доступа или ошибка ввода-вывода).";
    }
    finally
    {
      IsBusy = false;
    }
  }

  // Открытие зашифрованного архива отменено пользователем.
  internal void ShowPasswordCancelled()
  {
    ResetTree();
    StatusMessage = "Открытие отменено: для зашифрованного архива требуется пароль.";
  }

  // Чистая логика применения результата — без UI/IO, удобно тестировать.
  internal void ApplyResult(string archiveName, SevenZipArchiveDecodeResult result, SevenZipDecodedEntry[] entries)
  {
    if (result == SevenZipArchiveDecodeResult.Ok)
    {
      _root = BuildTree(entries);
      _current = _root;
      RefreshView();

      HasArchive = true;
      Title = $"{archiveName} — LzmaSharp";
      StatusMessage = entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();

    // Сюда NotSupported попадает уже после принятой расшифровки (неверный пароль даёт
    // InvalidData и обрабатывается повтором), поэтому пароль тут ни при чём — это
    // неподдерживаемая возможность формата (напр. фильтр BCJ2 для .exe).
    StatusMessage = result == SevenZipArchiveDecodeResult.NotSupported
        ? "Архив использует неподдерживаемую возможность формата (например, фильтр "
          + "BCJ2 для исполняемых файлов). Такой архив можно открыть в 7-Zip."
        : "Не удалось открыть: файл повреждён, не является поддерживаемым 7z-архивом "
          + "либо использует неподдерживаемое шифрование/фильтр (например, .exe под AES). "
          + "Такой архив можно открыть в 7-Zip.";
  }

  // Строит виртуальное дерево из путей записей (папки выводятся и из путей файлов).
  private static Node BuildTree(IEnumerable<SevenZipDecodedEntry> entries)
  {
    var root = new Node(string.Empty, isDirectory: true, parent: null);

    foreach (SevenZipDecodedEntry entry in entries)
    {
      string[] parts = entry.Name
          .Replace('\\', '/')
          .Split('/', StringSplitOptions.RemoveEmptyEntries);

      if (parts.Length == 0)
        continue;

      Node node = root;

      for (int i = 0; i < parts.Length; i++)
      {
        bool isLast = i == parts.Length - 1;
        bool isFile = isLast && !entry.IsDirectory;

        if (!node.Children.TryGetValue(parts[i], out Node? child))
        {
          child = new Node(parts[i], isDirectory: !isFile, parent: node);
          node.Children[parts[i]] = child;
        }

        if (isFile)
          child.Size = entry.Bytes.LongLength;

        node = child;
      }
    }

    return root;
  }

  private void ResetTree()
  {
    _root = new Node(string.Empty, isDirectory: true, parent: null);
    _current = _root;
    RefreshView();

    HasArchive = false;
    Title = DefaultTitle;
    _archiveBytes = null;
    _archivePassword = null;
  }

  // Пересобирает список текущей папки и навигационное состояние.
  private void RefreshView()
  {
    Items.Clear();

    foreach (Node child in _current.Children.Values
                 .OrderByDescending(n => n.IsDirectory)
                 .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
    {
      Items.Add(new ArchiveItem
      {
        Name = child.Name,
        IsDirectory = child.IsDirectory,
        Size = child.Size,
      });
    }

    CurrentPath = BuildCurrentPath();
    CanGoUp = _current.Parent is not null;
  }

  private string BuildCurrentPath()
  {
    var names = new Stack<string>();

    for (Node? n = _current; n is { Parent: not null }; n = n.Parent)
      names.Push(n.Name);

    return string.Join("/", names);
  }
}
