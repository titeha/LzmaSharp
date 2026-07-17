using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
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
  private readonly ICreatePasswordPrompt? _createPasswordPrompt;
  private readonly IFileSystemBrowser? _fileSystemBrowser;

  // Текущий каталог браузера ФС; null — показываем корни (диски / «Этот компьютер»).
  private string? _currentDirectory;

  // Число отмеченных галочкой элементов текущего списка (мультивыбор в браузере ФС).
  private int _selectedCount;

  // Байты и пароль успешно открытого архива — нужны для извлечения без повторного открытия.
  private byte[]? _archiveBytes;
  private string? _archivePassword;

  // Распакованные элементы открытого ZIP-архива (in-memory); null — открыт не-ZIP либо архив не
  // открыт. Если задан — источник для распаковки ZIP на диск.
  private Lzma.Core.Zip.ZipEntry[]? _zipEntries;

  // Путь к открытому «большому» архиву (обзор без загрузки в память); null — открыт in-memory либо
  // архив не открыт. Если задан — извлечение идёт потоковым путём из файла.
  private string? _archivePath;

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
  private string? _progressText;
  private string? _progressEta;
  private bool _isScanning;
  private string? _scanStatus;
  private string? _currentFileStatus;

  // Источник токена отмены текущей длительной операции; null — операция не идёт.
  private CancellationTokenSource? _operationCts;

  // Часы текущей длительной операции — для оценки оставшегося времени (ETA).
  private readonly Stopwatch _operationClock = new();

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
      ISourceFolderPicker? sourceFolderPicker,
      ICreatePasswordPrompt? createPasswordPrompt = null,
      IFileSystemBrowser? fileSystemBrowser = null)
  {
    _picker = picker;
    _passwordPrompt = passwordPrompt;
    _folderPicker = folderPicker;
    _archiveService = archiveService;
    _sourceFilesPicker = sourceFilesPicker;
    _saveFilePicker = saveFilePicker;
    _sourceFolderPicker = sourceFolderPicker;
    _createPasswordPrompt = createPasswordPrompt;
    _fileSystemBrowser = fileSystemBrowser;
    _current = _root;
    OpenCommand = new AsyncRelayCommand(OpenAsync);
    OpenArchiveFileCommand = new AsyncRelayCommand(OpenArchiveFileAsync, () => !IsOperating, this);
    NavigateUpCommand = new RelayCommand(NavigateUp, () => CanGoUp, this);
    ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync, () => HasArchive && !IsOperating, this);
    ExtractArchiveFileCommand = new AsyncRelayCommand(ExtractArchiveFileAsync, () => !IsOperating, this);
    CreateCommand = new AsyncRelayCommand(CreateFromFilesAsync, () => CanCreate && !IsOperating, this);
    CreateFromFolderCommand = new AsyncRelayCommand(CreateFromFolderAsync, () => CanCreateFromFolder && !IsOperating, this);
    CreateFromSelectionCommand = new AsyncRelayCommand(CreateFromSelectionAsync, () => CanCreateFromSelection && !IsOperating, this);
    CancelCommand = new RelayCommand(Cancel, () => IsOperating || IsScanning, this);

    // На старте (если шов ФС внедрён) показываем браузер файловой системы с корней.
    if (_fileSystemBrowser is not null)
      ShowFileSystem(null);
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
    set
    {
      if (Set(ref _statusMessage, value))
        OnPropertyChanged(nameof(IsBottomBarVisible));
    }
  }

  /// <summary>Открыт ли архив (есть содержимое для показа).</summary>
  public bool HasArchive
  {
    get => _hasArchive;
    set
    {
      if (Set(ref _hasArchive, value))
      {
        OnPropertyChanged(nameof(IsFileSystemMode));
        OnPropertyChanged(nameof(HasContent));
      }
    }
  }

  /// <summary>Активен ли режим браузера файловой системы (шов внедрён и архив не открыт).</summary>
  public bool IsFileSystemMode => _fileSystemBrowser is not null && !HasArchive;

  /// <summary>Есть ли что показывать в таблице: содержимое архива или список ФС.</summary>
  public bool HasContent => HasArchive || IsFileSystemMode;

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
    set
    {
      if (Set(ref _isBusy, value))
      {
        OnPropertyChanged(nameof(IsCancelVisible));
        OnPropertyChanged(nameof(IsBottomBarVisible));
      }
    }
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
  /// Живой текст объёма текущей операции («3.2 МБ / 10 МБ»); пусто при неизвестном общем
  /// размере. Показывается рядом с процентом, пока идёт операция (<see cref="IsBusy"/>).
  /// </summary>
  public string? ProgressText
  {
    get => _progressText;
    private set => Set(ref _progressText, value);
  }

  /// <summary>
  /// Оценка оставшегося времени текущей операции («осталось ~2 мин 5 с»); <see langword="null"/>,
  /// пока оценить нельзя (в самом начале или при неизвестном объёме). Оценка грубая — средняя
  /// скорость с начала операции, в первые секунды заметно прыгает. Показывается рядом с процентом.
  /// </summary>
  public string? ProgressEta
  {
    get => _progressEta;
    private set => Set(ref _progressEta, value);
  }

  /// <summary>
  /// Идёт ли сканирование/чтение исходных файлов в память (фаза до сжатия). Пока true —
  /// показываем живой счётчик <see cref="ScanStatus"/>.
  /// </summary>
  public bool IsScanning
  {
    get => _isScanning;
    private set
    {
      if (Set(ref _isScanning, value))
      {
        OnPropertyChanged(nameof(IsCancelVisible));
        OnPropertyChanged(nameof(IsBottomBarVisible));
      }
    }
  }

  /// <summary>
  /// Видима ли кнопка отмены: показываем и на фазе сжатия/извлечения (<see cref="IsBusy"/>),
  /// и на фазе сканирования исходных файлов (<see cref="IsScanning"/>).
  /// </summary>
  public bool IsCancelVisible => IsBusy || IsScanning;

  /// <summary>
  /// Видима ли нижняя панель: во время операции/сканирования (строка прогресса) либо когда есть
  /// статусное сообщение. Пусто — панель скрыта, чтобы не занимать место.
  /// </summary>
  public bool IsBottomBarVisible => IsCancelVisible || !string.IsNullOrEmpty(StatusMessage);

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
  /// Имя файла, сжимаемого прямо сейчас (как в 7-Zip), во время создания архива;
  /// <see langword="null"/> — скрыт (операция не идёт).
  /// </summary>
  public string? CurrentFileStatus
  {
    get => _currentFileStatus;
    private set => Set(ref _currentFileStatus, value);
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

  /// <summary>
  /// Команда «Открыть большой архив…» — обзор содержимого .7z по пути БЕЗ загрузки в память
  /// (для архивов больше 2 ГиБ). Извлечение потом идёт потоковым путём из файла.
  /// </summary>
  public AsyncRelayCommand OpenArchiveFileCommand { get; }

  /// <summary>Команда «Извлечь всё» — распаковать содержимое архива в выбранную папку.</summary>
  public AsyncRelayCommand ExtractAllCommand { get; }

  /// <summary>
  /// Команда «Извлечь архив с диска…» — выбрать .7z по пути и извлечь ПОТОКОВО, не загружая архив
  /// в память (для архивов больше 2 ГиБ). Не требует предварительного открытия/обзора.
  /// </summary>
  public AsyncRelayCommand ExtractArchiveFileCommand { get; }

  /// <summary>Команда «Создать архив…» — упаковать выбранные файлы в новый 7z-архив.</summary>
  public AsyncRelayCommand CreateCommand { get; }

  /// <summary>Команда «Создать из папки…» — упаковать содержимое выбранной папки (рекурсивно).</summary>
  public AsyncRelayCommand CreateFromFolderCommand { get; }

  /// <summary>Команда «Создать из выбранного» — упаковать отмеченные в браузере файлы и папки.</summary>
  public AsyncRelayCommand CreateFromSelectionCommand { get; }

  /// <summary>Команда «Отмена» — прерывает текущую длительную операцию (сжатие/извлечение).</summary>
  public RelayCommand CancelCommand { get; }

  /// <summary>Доступные методы сжатия для создания архива (с дружелюбными именами для UI).</summary>
  public IReadOnlyList<CompressionMethodOption> CompressionMethods { get; } =
  [
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Lzma2),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Ppmd),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Auto),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Bcj2),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Aes),
      CompressionMethodOption.ForMethod(SevenZipWriterCompressionMethod.Copy),
  ];

  private SevenZipWriterCompressionMethod _selectedCompressionMethod = SevenZipWriterCompressionMethod.Lzma2;

  /// <summary>Выбранный метод сжатия для создаваемого архива.</summary>
  public SevenZipWriterCompressionMethod SelectedCompressionMethod
  {
    get => _selectedCompressionMethod;
    set => Set(ref _selectedCompressionMethod, value);
  }

  /// <summary>Доступное число потоков сжатия (Авто + степени двойки до числа ядер).</summary>
  public IReadOnlyList<ThreadCountOption> ThreadCountOptions { get; } = BuildThreadCountOptions();

  private int _selectedThreadCount; // 0 = авто (все ядра)

  /// <summary>Выбранное число потоков сжатия (0 — авто/все ядра).</summary>
  public int SelectedThreadCount
  {
    get => _selectedThreadCount;
    set => Set(ref _selectedThreadCount, value);
  }

  /// <summary>Доступные размеры словаря LZMA2 (больше словарь — лучше сжатие, но больше памяти).</summary>
  public IReadOnlyList<DictionarySizeOption> DictionarySizeOptions { get; } =
  [
      new(1 << 20, "1 МБ"),
      new(1 << 22, "4 МБ (по умолчанию)"),
      new(1 << 24, "16 МБ"),
      new(1 << 26, "64 МБ"),
      new(1 << 28, "256 МБ"),
  ];

  private int _selectedDictionarySize = 1 << 22; // 4 МБ

  /// <summary>Выбранный размер словаря LZMA2 (байт) для потокового создания.</summary>
  public int SelectedDictionarySize
  {
    get => _selectedDictionarySize;
    set => Set(ref _selectedDictionarySize, value);
  }

  /// <summary>Доступные размеры тома (0 = один файл; иначе архив режется на base.001/.002/…).</summary>
  public IReadOnlyList<VolumeSizeOption> VolumeSizeOptions { get; } =
  [
      new(0, "Один файл (без томов)"),
      new(10L << 20, "10 МБ"),
      new(100L << 20, "100 МБ"),
      new(700L << 20, "700 МБ (CD)"),
      new(4692L << 20, "4692 МБ (DVD)"),
  ];

  private long _selectedVolumeSize; // 0 = один файл

  /// <summary>Выбранный размер тома (байт); 0 — не резать на тома.</summary>
  public long SelectedVolumeSize
  {
    get => _selectedVolumeSize;
    set => Set(ref _selectedVolumeSize, value);
  }

  // Строит список опций числа потоков: «Авто (N ядер)» + степени двойки 1..N.
  private static ThreadCountOption[] BuildThreadCountOptions()
  {
    int cores = Environment.ProcessorCount;
    var list = new List<ThreadCountOption> { new(0, $"Авто (все ядра: {cores})") };

    for (int n = 1; n <= cores; n *= 2)
      list.Add(new(n, n.ToString()));

    // Если число ядер не степень двойки — добавим само N в конец.
    if ((cores & (cores - 1)) != 0)
      list.Add(new(cores, cores.ToString()));

    return [.. list];
  }

  /// <summary>Доступно ли создание архива из файлов (внедрены ли соответствующие пикеры).</summary>
  public bool CanCreate => _sourceFilesPicker is not null && _saveFilePicker is not null;

  /// <summary>Доступно ли создание архива из папки (внедрены ли соответствующие пикеры).</summary>
  public bool CanCreateFromFolder => _sourceFolderPicker is not null && _saveFilePicker is not null;

  /// <summary>Доступно ли создание из выбранного в браузере (есть шов ФС, куда сохранять и что паковать).</summary>
  public bool CanCreateFromSelection => _fileSystemBrowser is not null && _saveFilePicker is not null && HasSelection;

  /// <summary>
  /// Активировать элемент (двойной клик): в браузере ФС файл-архив открывается, папка/диск —
  /// заход внутрь; в режиме архива — заход в папку. Общая точка для двойного клика из UI.
  /// </summary>
  public async Task ActivateItemAsync(ArchiveItem item)
  {
    if (item is null)
      return;

    // Браузер ФС: двойной клик по файлу-архиву открывает его.
    if (IsFileSystemMode && !item.IsDirectory)
    {
      if (item.IsArchiveFile && item.FullPath is not null)
        await OpenArchiveFromBrowserAsync(item);

      return; // прочие файлы двойным кликом пока не открываем
    }

    NavigateInto(item);
  }

  // Открывает архив по пути из браузера ФС (читает в память ≤2 ГиБ, дальше общий путь обработки).
  private async Task OpenArchiveFromBrowserAsync(ArchiveItem item)
  {
    if (_fileSystemBrowser is null || item.FullPath is not { } path)
      return;

    if (item.Size > int.MaxValue)
    {
      StatusMessage = "Архив больше 2 ГиБ — откройте его кнопкой «Открыть большой архив…».";
      return;
    }

    byte[] bytes;
    try
    {
      bytes = await Task.Run(() =>
      {
        using Stream stream = _fileSystemBrowser.OpenRead(path);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
      });
    }
    catch (IOException)
    {
      StatusMessage = "Не удалось прочитать файл архива.";
      return;
    }
    catch (UnauthorizedAccessException)
    {
      StatusMessage = "Нет доступа к файлу архива.";
      return;
    }

    await ProcessOpenedArchiveAsync(new PickedArchive(item.Name, bytes));
  }

  /// <summary>Войти в элемент: для папки — перейти внутрь; файлы пока игнорируются.</summary>
  public void NavigateInto(ArchiveItem item)
  {
    if (item is null)
      return;

    // Режим браузера ФС: заходим в папку/диск по полному пути.
    if (IsFileSystemMode)
    {
      if (item.IsDirectory && item.FullPath is { } path)
        ShowFileSystem(path);
      // Файлы (в т.ч. архивы) — открытие/заход подключим отдельным шагом.
      return;
    }

    // Режим архива: навигация по виртуальному дереву.
    if (!item.IsDirectory)
      return;

    if (_current.Children.TryGetValue(item.Name, out Node? child) && child.IsDirectory)
    {
      _current = child;
      RefreshView();
    }
  }

  /// <summary>Подняться на уровень вверх (в ФС — к родителю/корням; в архиве — по дереву).</summary>
  public void NavigateUp()
  {
    if (IsFileSystemMode)
    {
      if (_currentDirectory is { } dir)
        ShowFileSystem(_fileSystemBrowser!.GetParent(dir)); // null → к списку корней
      return;
    }

    // Режим архива: вверх по дереву; с корня архива (Parent == null) — закрываем архив и
    // возвращаемся в браузер ФС (как в 7-Zip: «вверх» из корня архива ведёт наружу).
    if (_current.Parent is { } parent)
    {
      _current = parent;
      RefreshView();
    }
    else if (_fileSystemBrowser is not null)
    {
      ResetTree();
    }
  }

  // Показывает содержимое каталога ФС (или список корней при directory=null) в общей таблице.
  private void ShowFileSystem(string? directory)
  {
    if (_fileSystemBrowser is null)
      return;

    _currentDirectory = directory;

    IReadOnlyList<FileSystemEntry> entries = directory is null
        ? _fileSystemBrowser.ListRoots()
        : _fileSystemBrowser.ListDirectory(directory);

    ClearItems();

    foreach (FileSystemEntry entry in entries
                 .OrderByDescending(e => e.IsDirectory)
                 .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
    {
      AddItem(new ArchiveItem
      {
        Name = entry.Name,
        IsDirectory = entry.IsDirectory,
        Size = entry.Size,
        FullPath = entry.FullPath,
      });
    }

    CurrentPath = directory ?? "Этот компьютер";
    CanGoUp = directory is not null;
  }

  /// <summary>Формат архива, определяемый по сигнатуре первых байт.</summary>
  internal enum ArchiveFormat
  {
    /// <summary>Неопознан (нет известной сигнатуры).</summary>
    Unknown,

    /// <summary>7z-контейнер (сигнатура <c>37 7A BC AF 27 1C</c>).</summary>
    SevenZip,

    /// <summary>ZIP-контейнер (сигнатура <c>PK\x03\x04</c> / <c>PK\x05\x06</c> / <c>PK\x07\x08</c>).</summary>
    Zip,
  }

  /// <summary>Определяет формат архива по сигнатуре (чистая функция, покрыта тестами).</summary>
  internal static ArchiveFormat DetectFormat(byte[] bytes)
  {
    if (bytes.Length >= 6 &&
        bytes[0] == 0x37 && bytes[1] == 0x7A && bytes[2] == 0xBC &&
        bytes[3] == 0xAF && bytes[4] == 0x27 && bytes[5] == 0x1C)
    {
      return ArchiveFormat.SevenZip;
    }

    // Все локальные варианты ZIP начинаются с "PK"; далее 03 04 (локальный заголовок),
    // 05 06 (пустой архив — сразу EOCD) или 07 08 (spanned/split).
    if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B &&
        ((bytes[2] == 0x03 && bytes[3] == 0x04) ||
         (bytes[2] == 0x05 && bytes[3] == 0x06) ||
         (bytes[2] == 0x07 && bytes[3] == 0x08)))
    {
      return ArchiveFormat.Zip;
    }

    return ArchiveFormat.Unknown;
  }

  private async Task OpenAsync()
  {
    PickedArchive? picked = await _picker.PickAsync();

    if (picked is null)
      return; // выбор отменён — состояние не трогаем

    await ProcessOpenedArchiveAsync(picked);
  }

  // Обрабатывает уже прочитанный в память архив (формат → zip/7z → при необходимости пароль).
  // Общий путь для «Открыть…» (диалог) и открытия архива из браузера ФС.
  private async Task ProcessOpenedArchiveAsync(PickedArchive picked)
  {
    // ZIP-контейнер обрабатываем отдельным путём (свой ридер, без пароля/потока).
    if (DetectFormat(picked.Bytes) == ArchiveFormat.Zip)
    {
      await OpenZipAsync(picked);
      return;
    }

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

  // Открытие ZIP-архива в память (свой ридер: Store/Deflate, без пароля/потока).
  private async Task OpenZipAsync(PickedArchive picked)
  {
    ZipOpenOutcome outcome = await _archiveService.OpenZipAsync(picked.Bytes);

    if (outcome.Result == ZipReadResult.Ok)
    {
      _root = BuildTree(outcome.Entries);
      _current = _root;
      RefreshView();

      HasArchive = true;
      Title = $"{picked.Name} — LzmaSharp";
      _archiveBytes = null;
      _archivePassword = null;
      _archivePath = null;
      _zipEntries = outcome.Entries; // источник для распаковки ZIP на диск
      StatusMessage = outcome.Entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();
    StatusMessage = outcome.Result == ZipReadResult.NotSupported
        ? "ZIP использует неподдерживаемую возможность: ZIP64 (архивы больше 4 ГиБ), шифрование "
          + "или метод сжатия, кроме Store/Deflate. Такой архив можно открыть в 7-Zip."
        : "Не удалось открыть ZIP: файл повреждён или не является поддерживаемым ZIP-архивом.";
  }

  // Обзор БОЛЬШОГО архива по пути: читаем только листинг (без распаковки и без загрузки в память).
  private async Task OpenArchiveFileAsync()
  {
    string? archivePath = await _picker.PickArchivePathAsync();

    if (archivePath is null)
      return; // выбор отменён / нет локального пути

    ArchiveListOutcome outcome = await _archiveService.OpenFromFileAsync(archivePath);

    if (outcome.Result == SevenZipArchiveDecodeResult.Ok)
    {
      _root = BuildTree(outcome.Entries);
      _current = _root;
      RefreshView();

      HasArchive = true;
      Title = $"{System.IO.Path.GetFileName(archivePath)} — LzmaSharp";
      _archiveBytes = null;
      _archivePassword = null;
      _archivePath = archivePath; // источник для потокового извлечения
      _zipEntries = null;
      StatusMessage = outcome.Entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();
    StatusMessage = outcome.Result == SevenZipArchiveDecodeResult.NotSupported
        ? "Этот архив нельзя открыть потоково (например, шифрование, закодированный заголовок или "
          + "сложные фильтры). Небольшой архив попробуйте через «Открыть…»."
        : "Не удалось открыть архив: файл повреждён или не является поддерживаемым 7z-архивом.";
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
    _archivePath = null; // in-memory открытие — потоковый источник не используем
    _zipEntries = null;  // открыт 7z — сбрасываем возможное состояние ZIP
  }

  // Извлечение содержимого открытого архива в выбранную папку.
  private async Task ExtractAllAsync()
  {
    // Открыт ZIP — своя in-memory распаковка (Store/Deflate).
    if (_zipEntries is { } zipEntries)
    {
      await ExtractZipAsync(zipEntries);
      return;
    }

    if (_archivePath is null && _archiveBytes is null)
      return;

    string? destination = await _folderPicker.PickFolderAsync();

    if (destination is null)
      return; // выбор папки отменён

    IProgress<SevenZipProgress> progress = CreateProgress();
    var currentFile = new Progress<string>(name => CurrentFileStatus = FormatExtractingFileStatus(name));

    // Открыт как «большой» (потоковый) архив — извлекаем прямо из файла, не грузя в память.
    if (_archivePath is { } archivePath)
    {
      (bool proceed, string? streamPassword, bool encrypted) = await ResolveStreamingExtractPasswordAsync(archivePath);
      if (!proceed)
      {
        StatusMessage = "Извлечение отменено: для зашифрованного архива нужен пароль.";
        return;
      }

      await RunOperationAsync(async token =>
      {
        try
        {
          SevenZipArchiveDecodeResult result = await _archiveService.ExtractArchiveFileAsync(archivePath, destination, progress, token, currentFile, streamPassword);
          StatusMessage = StreamingExtractStatus(result, destination, encrypted);
        }
        finally { CurrentFileStatus = null; }
      });
      return;
    }

    byte[] bytes = _archiveBytes!;
    string? password = _archivePassword;

    await RunOperationAsync(async token =>
    {
      try
      {
        SevenZipArchiveDecodeResult result = await _archiveService.ExtractAllAsync(bytes, password, destination, progress, token, currentFile);
        StatusMessage = ExtractStatus(result, destination);
      }
      finally { CurrentFileStatus = null; }
    });
  }

  private static string ExtractStatus(SevenZipArchiveDecodeResult result, string destination) => result switch
  {
    SevenZipArchiveDecodeResult.Ok => $"Извлечено в: {destination}",
    SevenZipArchiveDecodeResult.NotSupported => "Извлечение не поддерживается для этого архива.",
    _ => "Не удалось извлечь: ошибка данных или файл уже существует.",
  };

  // Распаковка открытого ZIP на диск (уже прочитанные элементы, in-memory).
  private async Task ExtractZipAsync(ZipEntry[] entries)
  {
    string? destination = await _folderPicker.PickFolderAsync();

    if (destination is null)
      return; // выбор папки отменён

    var currentFile = new Progress<string>(name => CurrentFileStatus = FormatExtractingFileStatus(name));

    await RunOperationAsync(async token =>
    {
      try
      {
        ZipExtractResult result = await _archiveService.ExtractZipAsync(entries, destination, token, currentFile);
        StatusMessage = ZipExtractStatus(result, destination);
      }
      finally { CurrentFileStatus = null; }
    });
  }

  internal static string ZipExtractStatus(ZipExtractResult result, string destination) => result switch
  {
    ZipExtractResult.Ok => $"Извлечено в: {destination}",
    ZipExtractResult.IOError => "Не удалось извлечь ZIP: ошибка записи на диск.",
    _ => "Не удалось извлечь ZIP: небезопасный путь, конфликт имён или файл уже существует.",
  };

  // Итог потокового извлечения по пути с учётом шифрования: при неудаче зашифрованного архива
  // подсказываем про пароль.
  private static string StreamingExtractStatus(SevenZipArchiveDecodeResult result, string destination, bool encrypted)
  {
    if (result == SevenZipArchiveDecodeResult.Ok)
      return $"Извлечено в: {destination}";

    if (encrypted)
      return "Не удалось извлечь: неверный пароль или повреждённый архив.";

    return ExtractStatus(result, destination);
  }

  // Для потокового извлечения по пути: определяет шифрование и (если зашифрован) спрашивает пароль
  // ДО операции. Возвращает: продолжать ли, введённый пароль, признак шифрования.
  private async Task<(bool Proceed, string? Password, bool Encrypted)> ResolveStreamingExtractPasswordAsync(string archivePath)
  {
    bool encrypted = await _archiveService.IsArchiveEncryptedAsync(archivePath);
    if (!encrypted)
      return (true, null, false);

    string? password = await _passwordPrompt.RequestAsync(Path.GetFileName(archivePath), previousAttemptFailed: false);
    return password is null ? (false, null, true) : (true, password, true);
  }

  // Прямое потоковое извлечение архива с диска (без открытия/обзора) — для архивов > 2 ГиБ.
  private async Task ExtractArchiveFileAsync()
  {
    string? archivePath = await _picker.PickArchivePathAsync();

    if (archivePath is null)
      return; // выбор архива отменён / нет локального пути

    string? destination = await _folderPicker.PickFolderAsync();

    if (destination is null)
      return; // выбор папки отменён

    (bool proceed, string? password, bool encrypted) = await ResolveStreamingExtractPasswordAsync(archivePath);
    if (!proceed)
    {
      StatusMessage = "Извлечение отменено: для зашифрованного архива нужен пароль.";
      return;
    }

    IProgress<SevenZipProgress> progress = CreateProgress();
    var currentFile = new Progress<string>(name => CurrentFileStatus = FormatExtractingFileStatus(name));

    await RunOperationAsync(async token =>
    {
      try
      {
        SevenZipArchiveDecodeResult result = await _archiveService.ExtractArchiveFileAsync(archivePath, destination, progress, token, currentFile, password);
        StatusMessage = StreamingExtractStatus(result, destination, encrypted);
      }
      finally { CurrentFileStatus = null; }
    });
  }

  // Создание из выбранных файлов.
  private async Task CreateFromFilesAsync()
  {
    if (_sourceFilesPicker is null)
      return;

    if (UseStreamingCreate(_sourceFilesPicker.SupportsRefs))
      await CreateStreamingFromSourceAsync(_sourceFilesPicker.PickFileRefsAsync);
    else
      await CreateFromSourceAsync(_sourceFilesPicker.PickFilesAsync);
  }

  // Создание из выбранной папки (рекурсивно, с относительными путями).
  private async Task CreateFromFolderAsync()
  {
    if (_sourceFolderPicker is null)
      return;

    if (UseStreamingCreate(_sourceFolderPicker.SupportsRefs))
      await CreateStreamingFromSourceAsync(_sourceFolderPicker.PickFolderFileRefsAsync);
    else
      await CreateFromSourceAsync(_sourceFolderPicker.PickFolderFilesAsync);
  }

  // Создание из отмеченных в браузере ФС файлов и папок (потоково, файлы читаются лениво).
  private async Task CreateFromSelectionAsync()
  {
    if (_fileSystemBrowser is null || !HasSelection)
      return;

    // Снимок выбранных путей на момент запуска (список может измениться при навигации).
    IReadOnlyList<string> paths = SelectedPaths;

    await CreateStreamingFromSourceAsync((scanProgress, token) => Task.Run<IReadOnlyList<PickedFileRef>?>(() =>
    {
      IReadOnlyList<ArchiveSourceFile> sources = _fileSystemBrowser.EnumerateForArchive(paths);

      var refs = new List<PickedFileRef>(sources.Count);
      long bytes = 0;
      foreach (ArchiveSourceFile source in sources)
      {
        token.ThrowIfCancellationRequested();
        string full = source.FullPath;
        refs.Add(new PickedFileRef(source.EntryName, source.Length, () => _fileSystemBrowser.OpenRead(full)));
        bytes += source.Length;
        scanProgress?.Report(new ScanProgress(refs.Count, bytes));
      }

      return refs;
    }, token));
  }

  // Потоковое создание доступно для ВСЕХ методов (LZMA2 многопоточно; PPMd/Copy — пофайлово), если
  // пикер умеет отдавать ссылки на файлы (без чтения в память) — так паковать можно и файлы > 2 ГиБ.
  private bool UseStreamingCreate(bool pickerSupportsRefs)
      => pickerSupportsRefs;

  // Общий путь создания: получить источник → выбрать путь → собрать ядром → записать на диск.
  private async Task CreateFromSourceAsync(
      Func<IProgress<ScanProgress>?, CancellationToken, Task<IReadOnlyList<PickedFile>?>> pickSources)
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

    // Сканирование можно прервать кнопкой «Отмена»: свой источник токена на время фазы,
    // виден команде отмены через _operationCts (кнопка доступна, пока IsScanning).
    IReadOnlyList<PickedFile>? files;
    using (var scanCts = new CancellationTokenSource())
    {
      _operationCts = scanCts;
      try
      {
        files = await pickSources(scanProgress, scanCts.Token);
      }
      catch (OperationCanceledException)
      {
        StatusMessage = "Операция отменена.";
        return;
      }
      finally
      {
        _operationCts = null;
        IsScanning = false;
        ScanStatus = null;
      }
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

      long originalBytes = 0;
      foreach (PickedFile file in files)
        originalBytes += file.Bytes.LongLength;

      StatusMessage = wrote
          ? FormatCreateSummary(path, originalBytes, created.Archive.LongLength)
          : "Архив собран, но записать на диск не удалось (нет доступа или ошибка ввода-вывода).";
    });
  }

  // Потоковое создание: получить ССЫЛКИ на файлы (без чтения в память) → выбрать путь → собрать
  // архив ядром прямо в целевой файл потоком. Для файлов > 2 ГиБ (LZMA2).
  private async Task CreateStreamingFromSourceAsync(
      Func<IProgress<ScanProgress>?, CancellationToken, Task<IReadOnlyList<PickedFileRef>?>> pickRefs)
  {
    if (_saveFilePicker is null)
      return;

    var scanProgress = new DelegateProgress<ScanProgress>(sp =>
    {
      IsScanning = true;
      ScanStatus = FormatScanStatus(sp);
    });

    IReadOnlyList<PickedFileRef>? files;
    using (var scanCts = new CancellationTokenSource())
    {
      _operationCts = scanCts;
      try
      {
        files = await pickRefs(scanProgress, scanCts.Token);
      }
      catch (OperationCanceledException)
      {
        StatusMessage = "Операция отменена.";
        return;
      }
      finally
      {
        _operationCts = null;
        IsScanning = false;
        ScanStatus = null;
      }
    }

    if (files is null || files.Count == 0)
      return;

    string? path = await _saveFilePicker.PickSavePathAsync("archive.7z");

    if (path is null)
      return;

    // Для AES спрашиваем пароль (с подтверждением) ДО начала операции; отмена — не создаём.
    string? password = null;
    if (SelectedCompressionMethod == SevenZipWriterCompressionMethod.Aes)
    {
      password = _createPasswordPrompt is null ? null : await _createPasswordPrompt.RequestNewPasswordAsync();
      if (password is null)
      {
        StatusMessage = "Создание отменено: для шифрования нужен пароль.";
        return;
      }
    }

    IProgress<SevenZipProgress> progress = CreateProgress();
    // Метку кодека маршалим в UI (Progress), а счётчики кодеков считаем синхронно — ядро зовёт
    // Report в своём потоке ДО возврата, поэтому к моменту завершения create счётчики точны.
    var label = new Progress<string>(text => CurrentFileStatus = text);
    var currentFile = new CodecTallyProgress(label);

    await RunOperationAsync(async token =>
    {
      var entries = new List<SevenZipStreamingEntry>(files.Count);
      long originalBytes = 0;

      foreach (PickedFileRef file in files)
      {
        entries.Add(new SevenZipStreamingEntry(file.Name, file.Length, file.OpenRead));
        originalBytes += file.Length;
      }

      SevenZipArchiveWriteResult result;
      try
      {
        result = await _archiveService.CreateArchiveToFileAsync(
            entries, path, SelectedCompressionMethod, SelectedDictionarySize, SelectedThreadCount, progress, token, currentFile, SelectedVolumeSize, password);
      }
      finally
      {
        CurrentFileStatus = null;
      }

      if (result != SevenZipArchiveWriteResult.Ok)
      {
        StatusMessage = result == SevenZipArchiveWriteResult.NotSupported
            ? "Потоковое создание с такими параметрами не поддерживается."
            : "Не удалось создать архив: ошибка ввода-вывода или некорректный набор файлов.";
        return;
      }

      long compressedBytes = 0;
      int volumeCount = 0;
      if (SelectedVolumeSize > 0)
      {
        // Тома: файла `path` нет — есть path.001/.002/…; суммируем их размеры и считаем количество.
        for (int vi = 0; ; vi++)
        {
          string volumePath = VolumeSpanningWriteStream.VolumePath(path, vi);
          if (!File.Exists(volumePath))
            break;
          try { compressedBytes += new FileInfo(volumePath).Length; }
          catch (IOException) { }
          volumeCount++;
        }
      }
      else
      {
        try { compressedBytes = new FileInfo(path).Length; }
        catch (IOException) { }
      }

      StatusMessage = FormatCreateSummary(path, originalBytes, compressedBytes);
      if (volumeCount > 0)
        StatusMessage += $"  Томов: {volumeCount} (по {ByteSizeFormat.Format(SelectedVolumeSize)}).";

      // Для «Авто» — разбивка, какими кодеками сжаты файлы (остаётся на экране, в отличие от
      // бегущей строки). Точна: счётчики набраны синхронно во время create.
      if (SelectedCompressionMethod == SevenZipWriterCompressionMethod.Auto)
      {
        string breakdown = FormatCodecBreakdown(currentFile.Counts);
        if (breakdown.Length != 0)
          StatusMessage += "\n" + breakdown;
      }
    });
  }

  // Синхронный подсчёт кодеков + маршалинг метки текущего файла в UI. Ядро вызывает Report
  // последовательно в рабочем потоке (пофайловый цикл), поэтому lock не обязателен, но дёшев и
  // страхует. Метку отдаём в переданный маршалящий IProgress.
  private sealed class CodecTallyProgress(IProgress<string> label) : IProgress<SevenZipCompressionFileProgress>
  {
    private readonly Dictionary<string, int> _counts = new();

    public IReadOnlyDictionary<string, int> Counts => _counts;

    public void Report(SevenZipCompressionFileProgress value)
    {
      lock (_counts)
        _counts[value.Codec] = (_counts.TryGetValue(value.Codec, out int c) ? c : 0) + 1;

      label.Report(FormatCurrentFileStatus(value.Name, value.Codec));
    }
  }

  // Форматирует живой счётчик сканирования. internal — для тестов.
  internal static string FormatScanStatus(ScanProgress p)
      => $"Сканирование: {p.FilesRead} {PluralizeFiles(p.FilesRead)}, {ByteSizeFormat.Format(p.BytesRead)}";

  // Строка «сжимается прямо сейчас» (как в 7-Zip), с меткой кодека. internal — для тестов.
  internal static string FormatCurrentFileStatus(string name, string codec) => $"Сжатие [{codec}]: {name}";

  // Разбивка по кодекам для «Авто» («Авто: PPMd — 12, LZMA2 — 40, Copy — 8»); нулевые опускаем,
  // порядок фиксирован. Пусто, если файлов не было. internal — для тестов.
  internal static string FormatCodecBreakdown(IReadOnlyDictionary<string, int> counts)
  {
    string[] order = ["PPMd", "LZMA2", "Copy"];
    var parts = new List<string>();
    foreach (string codec in order)
      if (counts.TryGetValue(codec, out int n) && n > 0)
        parts.Add($"{codec} — {n}");

    return parts.Count == 0 ? string.Empty : "Авто: " + string.Join(", ", parts);
  }

  // Строка «извлекается прямо сейчас». internal — для тестов.
  internal static string FormatExtractingFileStatus(string name) => $"Извлечение: {name}";

  // Итог создания архива: путь + размеры и коэффициент сжатия. internal — для тестов.
  internal static string FormatCreateSummary(string path, long originalBytes, long compressedBytes)
  {
    if (originalBytes <= 0 || compressedBytes <= 0)
      return $"Создан архив: {path} ({ByteSizeFormat.Format(compressedBytes)})";

    double ratio = (double)originalBytes / compressedBytes;

    return $"Создан архив: {path} (было {ByteSizeFormat.Format(originalBytes)} → "
         + $"стало {ByteSizeFormat.Format(compressedBytes)}, {ratio:0.0}×)";
  }

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

  // Преобразует отчёт ядра в процент/текст объёма/ETA и обновляет свойства. ETA считается по
  // истёкшему времени операции (_operationClock). internal-перегрузка с явным elapsed — для тестов.
  internal void ReportProgress(SevenZipProgress progress)
      => ReportProgress(progress, _operationClock.Elapsed);

  internal void ReportProgress(SevenZipProgress progress, TimeSpan elapsed)
  {
    ProgressPercent = ToPercent(progress);
    ProgressText = FormatProgressText(progress);

    TimeSpan? remaining = EstimateRemaining(progress, elapsed);
    ProgressEta = remaining is { } r ? FormatRemaining(r) : null;
  }

  // Живой текст объёма «обработано / всего»; пусто при неизвестном общем размере. internal — для тестов.
  internal static string FormatProgressText(SevenZipProgress progress)
      => progress.TotalBytes <= 0
          ? string.Empty
          : $"{ByteSizeFormat.Format(progress.BytesProcessed)} / {ByteSizeFormat.Format(progress.TotalBytes)}";

  // Чистое преобразование: доля обработанных байт → проценты, ограничено [0..100].
  // Неизвестный объём (TotalBytes <= 0) трактуем как 0 % (индикатор остаётся «неопределённым»).
  internal static double ToPercent(SevenZipProgress progress)
  {
    if (progress.TotalBytes <= 0)
      return 0;

    double percent = 100.0 * progress.BytesProcessed / progress.TotalBytes;
    return percent < 0 ? 0 : percent > 100 ? 100 : percent;
  }

  // Оценка оставшегося времени по средней скорости с начала операции. internal — для тестов.
  // null — оценить нельзя: ничего не обработано, неизвестен объём или ещё не прошло времени.
  // Оценка грубая (средняя скорость с начала); в первые секунды заметно прыгает.
  internal static TimeSpan? EstimateRemaining(SevenZipProgress progress, TimeSpan elapsed)
  {
    if (progress.BytesProcessed <= 0 || progress.TotalBytes <= 0 || elapsed <= TimeSpan.Zero)
      return null;

    long remainingBytes = progress.TotalBytes - progress.BytesProcessed;
    if (remainingBytes <= 0)
      return TimeSpan.Zero; // всё обработано (или переотчёт сверх объёма)

    // скорость = обработано / elapsed; осталось (сек) = remainingBytes / скорость.
    double remainingSeconds = remainingBytes * elapsed.TotalSeconds / progress.BytesProcessed;

    // Защита от переполнения TimeSpan при крошечной обработанной доле.
    return remainingSeconds > TimeSpan.MaxValue.TotalSeconds
        ? TimeSpan.MaxValue
        : TimeSpan.FromSeconds(remainingSeconds);
  }

  // Человекочитаемая оценка оставшегося времени («осталось ~2 мин 5 с»). internal — для тестов.
  internal static string FormatRemaining(TimeSpan remaining)
  {
    if (remaining < TimeSpan.Zero)
      remaining = TimeSpan.Zero;

    long totalSeconds = (long)Math.Round(remaining.TotalSeconds);

    if (totalSeconds >= 3600)
    {
      long hours = totalSeconds / 3600;
      long minutes = totalSeconds % 3600 / 60;
      return $"осталось ~{hours} ч {minutes} мин";
    }

    if (totalSeconds >= 60)
    {
      long minutes = totalSeconds / 60;
      long seconds = totalSeconds % 60;
      return $"осталось ~{minutes} мин {seconds} с";
    }

    return $"осталось ~{totalSeconds} с";
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
    ProgressText = null;
    ProgressEta = null;
    _operationClock.Restart();

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
      _operationClock.Stop();
      _operationCts = null;
      IsBusy = false;
      IsOperating = false;
      ProgressPercent = 0;
      ProgressText = null;
      ProgressEta = null;
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

  // Строит виртуальное дерево из декодированных записей (in-memory открытие).
  private static Node BuildTree(IEnumerable<SevenZipDecodedEntry> entries)
      => BuildTreeCore(entries.Select(e => (e.Name, e.IsDirectory, e.Bytes.LongLength)));

  // Строит виртуальное дерево из листинга (обзор большого архива без распаковки).
  private static Node BuildTree(IEnumerable<SevenZipListedEntry> entries)
      => BuildTreeCore(entries.Select(e => (e.Name, e.IsDirectory, e.Size)));

  // Строит виртуальное дерево из распакованных ZIP-элементов.
  private static Node BuildTree(IEnumerable<ZipEntry> entries)
      => BuildTreeCore(entries.Select(e => (e.Name, e.IsDirectory, e.Bytes.LongLength)));

  // Общее построение дерева из (имя, признак каталога, размер). Папки выводятся и из путей файлов.
  private static Node BuildTreeCore(IEnumerable<(string Name, bool IsDirectory, long Size)> items)
  {
    var root = new Node(string.Empty, isDirectory: true, parent: null);

    foreach ((string name, bool isDirectory, long size) in items)
    {
      string[] parts = name
          .Replace('\\', '/')
          .Split('/', StringSplitOptions.RemoveEmptyEntries);

      if (parts.Length == 0)
        continue;

      Node node = root;

      for (int i = 0; i < parts.Length; i++)
      {
        bool isLast = i == parts.Length - 1;
        bool isFile = isLast && !isDirectory;

        if (!node.Children.TryGetValue(parts[i], out Node? child))
        {
          child = new Node(parts[i], isDirectory: !isFile, parent: node);
          node.Children[parts[i]] = child;
        }

        if (isFile)
          child.Size = size;

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
    _archivePath = null;
    _zipEntries = null;

    // Если доступен браузер ФС — возвращаемся к нему (на тот же каталог), а не к пустому состоянию.
    if (_fileSystemBrowser is not null)
      ShowFileSystem(_currentDirectory);
  }

  // Пересобирает список текущей папки и навигационное состояние.
  private void RefreshView()
  {
    ClearItems();

    foreach (Node child in _current.Children.Values
                 .OrderByDescending(n => n.IsDirectory)
                 .ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
    {
      AddItem(new ArchiveItem
      {
        Name = child.Name,
        IsDirectory = child.IsDirectory,
        Size = child.Size,
      });
    }

    CurrentPath = BuildCurrentPath();
    // На корне архива «Вверх» доступен, если есть браузер ФС — чтобы можно было выйти из архива.
    CanGoUp = _current.Parent is not null || _fileSystemBrowser is not null;
  }

  /// <summary>Число отмеченных галочкой элементов текущего списка.</summary>
  public int SelectedCount
  {
    get => _selectedCount;
    private set
    {
      if (Set(ref _selectedCount, value))
        OnPropertyChanged(nameof(HasSelection));
    }
  }

  /// <summary>Есть ли отмеченные элементы.</summary>
  public bool HasSelection => SelectedCount > 0;

  /// <summary>Полные пути отмеченных элементов ФС (папки и файлы) — для действий над выбором.</summary>
  public IReadOnlyList<string> SelectedPaths =>
      [.. Items.Where(i => i.IsSelected && i.FullPath is not null).Select(i => i.FullPath!)];

  // Очищает список, отписываясь от уведомлений выбора (без утечек), и сбрасывает счётчик.
  private void ClearItems()
  {
    foreach (ArchiveItem item in Items)
      item.PropertyChanged -= OnItemPropertyChanged;

    Items.Clear();
    SelectedCount = 0;
  }

  // Добавляет элемент и подписывается на изменение его галочки.
  private void AddItem(ArchiveItem item)
  {
    item.PropertyChanged += OnItemPropertyChanged;
    Items.Add(item);
  }

  private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(ArchiveItem.IsSelected))
      SelectedCount = Items.Count(i => i.IsSelected);
  }

  private string BuildCurrentPath()
  {
    var names = new Stack<string>();

    for (Node? n = _current; n is { Parent: not null }; n = n.Parent)
      names.Push(n.Name);

    return string.Join("/", names);
  }
}
