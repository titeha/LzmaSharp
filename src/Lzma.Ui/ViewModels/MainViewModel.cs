using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;

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
  private readonly ISourceFolderPicker? _sourceFolderPicker;
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
  private bool _isOperating;
  private double _progressPercent;
  private bool _isScanning;
  private string? _scanStatus;

  // Источник токена отмены текущей длительной операции; null — операция не идёт.
  private CancellationTokenSource? _operationCts;

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
      : this(picker, passwordPrompt, folderPicker, archiveService, sourceFilesPicker, saveFilePicker, sourceFolderPicker: null)
  {
  }

  public MainViewModel(
      IArchivePicker picker,
      IPasswordPrompt passwordPrompt,
      IFolderPicker folderPicker,
      IArchiveService archiveService,
      ISourceFilesPicker? sourceFilesPicker,
      ISaveFilePicker? saveFilePicker,
      ISourceFolderPicker? sourceFolderPicker)
  {
    _picker = picker;
    _passwordPrompt = passwordPrompt;
    _folderPicker = folderPicker;
    _archiveService = archiveService;
    _sourceFilesPicker = sourceFilesPicker;
    _saveFilePicker = saveFilePicker;
    _sourceFolderPicker = sourceFolderPicker;
    _current = _root;
    OpenCommand = new AsyncRelayCommand(OpenAsync);
    NavigateUpCommand = new RelayCommand(NavigateUp, () => CanGoUp, this);
    ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync, () => HasArchive && !IsOperating, this);
    CreateCommand = new AsyncRelayCommand(CreateFromFilesAsync, () => CanCreate && !IsOperating, this);
    CreateFromFolderCommand = new AsyncRelayCommand(CreateFromFolderAsync, () => CanCreateFromFolder && !IsOperating, this);
    CancelCommand = new RelayCommand(Cancel, () => IsOperating, this);
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

  /// <summary>
  /// Визуальный индикатор «занято»: включается только если операция длится дольше
  /// <see cref="BusyIndicatorDelay"/> (быстрые операции индикатор не показывают).
  /// </summary>
  public bool IsBusy
  {
    get => _isBusy;
    set => Set(ref _isBusy, value);
  }

  /// <summary>
  /// Идёт ли длительная операция (извлечение/создание). Ставится сразу при старте и
  /// блокирует повторный запуск команд; в отличие от <see cref="IsBusy"/> не привязан к UI.
  /// </summary>
  public bool IsOperating
  {
    get => _isOperating;
    private set => Set(ref _isOperating, value);
  }

  /// <summary>
  /// Процент выполнения текущей длительной операции (0..100). Имеет смысл, пока идёт
  /// операция; в UI показывается вместе с <see cref="IsBusy"/>.
  /// </summary>
  public double ProgressPercent
  {
    get => _progressPercent;
    private set => Set(ref _progressPercent, value);
  }

  /// <summary>
  /// Идёт ли сканирование/чтение исходных файлов в память (фаза до сжатия). Пока true —
  /// показываем живой счётчик <see cref="ScanStatus"/>.
  /// </summary>
  public bool IsScanning
  {
    get => _isScanning;
    private set => Set(ref _isScanning, value);
  }

  /// <summary>
  /// Живой текст счётчика сканирования («Сканирование: N файлов, X МБ»);
  /// <see langword="null"/> — скрыт.
  /// </summary>
  public string? ScanStatus
  {
    get => _scanStatus;
    private set => Set(ref _scanStatus, value);
  }

  /// <summary>
  /// Порог, после которого показывается индикатор занятости. По умолчанию 3 секунды;
  /// internal — для подмены в тестах.
  /// </summary>
  internal TimeSpan BusyIndicatorDelay { get; set; } = TimeSpan.FromSeconds(3);

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

  /// <summary>Команда «Создать из папки…» — упаковать содержимое выбранной папки (рекурсивно).</summary>
  public AsyncRelayCommand CreateFromFolderCommand { get; }

  /// <summary>Команда «Отмена» — прерывает текущую длительную операцию (сжатие/извлечение).</summary>
  public RelayCommand CancelCommand { get; }

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

  /// <summary>Доступно ли создание архива из файлов (внедрены ли соответствующие пикеры).</summary>
  public bool CanCreate => _sourceFilesPicker is not null && _saveFilePicker is not null;

  /// <summary>Доступно ли создание архива из папки (внедрены ли соответствующие пикеры).</summary>
  public bool CanCreateFromFolder => _sourceFolderPicker is not null && _saveFilePicker is not null;

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

    IProgress<SevenZipProgress> progress = CreateProgress();

    await RunOperationAsync(async token =>
    {
      SevenZipArchiveDecodeResult result = await _archiveService.ExtractAllAsync(bytes, password, destination, progress, token);

      StatusMessage = result switch
      {
        SevenZipArchiveDecodeResult.Ok => $"Извлечено в: {destination}",
        SevenZipArchiveDecodeResult.NotSupported => "Извлечение не поддерживается для этого архива.",
        _ => "Не удалось извлечь: ошибка данных или файл уже существует.",
      };
    });
  }

  // Создание из выбранных файлов.
  private async Task CreateFromFilesAsync()
  {
    if (_sourceFilesPicker is null)
      return;

    await CreateFromSourceAsync(_sourceFilesPicker.PickFilesAsync);
  }

  // Создание из выбранной папки (рекурсивно, с относительными путями).
  private async Task CreateFromFolderAsync()
  {
    if (_sourceFolderPicker is null)
      return;

    await CreateFromSourceAsync(_sourceFolderPicker.PickFolderFilesAsync);
  }

  // Общий путь создания: получить источник → выбрать путь → собрать ядром → записать на диск.
  private async Task CreateFromSourceAsync(
      Func<IProgress<ScanProgress>?, Task<IReadOnlyList<PickedFile>?>> pickSources)
  {
    if (_saveFilePicker is null)
      return;

    // Живой счётчик на фазе сканирования/чтения файлов в память (до сжатия). Синхронный
    // адаптер: отчёты приходят на UI-поток по мере чтения, индикатор гаснет в finally.
    var scanProgress = new DelegateProgress<ScanProgress>(sp =>
    {
      IsScanning = true;
      ScanStatus = FormatScanStatus(sp);
    });

    IReadOnlyList<PickedFile>? files;
    try
    {
      files = await pickSources(scanProgress);
    }
    finally
    {
      IsScanning = false;
      ScanStatus = null;
    }

    if (files is null || files.Count == 0)
      return; // выбор отменён или ничего не выбрано

    string? path = await _saveFilePicker.PickSavePathAsync("archive.7z");

    if (path is null)
      return; // выбор пути отменён

    IProgress<SevenZipProgress> progress = CreateProgress();

    await RunOperationAsync(async token =>
    {
      var entries = new List<SevenZipArchiveWriterEntry>(files.Count);

      foreach (PickedFile file in files)
        entries.Add(new SevenZipArchiveWriterEntry(file.Name, file.Bytes));

      ArchiveCreateOutcome created = await _archiveService.CreateArchiveAsync(entries, SelectedCompressionMethod, progress, token);

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
    });
  }

  // Форматирует живой счётчик сканирования. internal — для тестов.
  internal static string FormatScanStatus(ScanProgress p)
      => $"Сканирование: {p.FilesRead} {PluralizeFiles(p.FilesRead)}, {ByteSizeFormat.Format(p.BytesRead)}";

  // Русское склонение слова «файл» по числу (1 файл, 2 файла, 5 файлов).
  internal static string PluralizeFiles(int count)
  {
    int mod100 = count % 100;
    if (mod100 is >= 11 and <= 14)
      return "файлов";

    return (count % 10) switch
    {
      1 => "файл",
      2 or 3 or 4 => "файла",
      _ => "файлов",
    };
  }

  // Преобразует отчёт ядра в процент (0..100) и обновляет ProgressPercent. internal — для тестов.
  internal void ReportProgress(SevenZipProgress progress) => ProgressPercent = ToPercent(progress);

  // Чистое преобразование: доля обработанных байт → проценты, ограничено [0..100].
  // Неизвестный объём (TotalBytes <= 0) трактуем как 0 % (индикатор остаётся «неопределённым»).
  internal static double ToPercent(SevenZipProgress progress)
  {
    if (progress.TotalBytes <= 0)
      return 0;

    double percent = 100.0 * progress.BytesProcessed / progress.TotalBytes;
    return percent < 0 ? 0 : percent > 100 ? 100 : percent;
  }

  // Создаёт мост прогресса: Progress<T> захватывает текущий SynchronizationContext (UI-поток
  // в реальном приложении), поэтому обновления свойства приходят на UI-поток.
  private IProgress<SevenZipProgress> CreateProgress() => new Progress<SevenZipProgress>(ReportProgress);

  // Прерывает текущую длительную операцию (если идёт).
  private void Cancel() => _operationCts?.Cancel();

  // Выполняет длительную операцию с отложенным индикатором: IsOperating ставится сразу
  // (блокирует повторный запуск), а визуальный IsBusy включается, только если операция
  // не завершилась за BusyIndicatorDelay. Так быстрые операции не показывают индикатор.
  // Операция получает CancellationToken; отмена ловится и показывается в статусе.
  private async Task RunOperationAsync(Func<CancellationToken, Task> operation)
  {
    IsOperating = true;
    ProgressPercent = 0;

    using var cts = new CancellationTokenSource();
    _operationCts = cts;

    Task work = operation(cts.Token);

    try
    {
      Task finished = await Task.WhenAny(work, Task.Delay(BusyIndicatorDelay));

      if (!work.IsCompleted && finished != work)
        IsBusy = true; // превысили порог — показываем индикатор

      await work; // дождаться завершения и проброса исключений
    }
    catch (OperationCanceledException)
    {
      StatusMessage = "Операция отменена.";
    }
    finally
    {
      _operationCts = null;
      IsBusy = false;
      IsOperating = false;
      ProgressPercent = 0;
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
