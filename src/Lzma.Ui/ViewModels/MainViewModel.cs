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

  // Узлы дерева ФС, на изменение выбора которых мы подписаны (для подсчёта выбора); отписываемся при
  // пересборке дерева, чтобы не текло.
  private readonly List<TreeNodeItem> _trackedTreeNodes = [];
  private TreeNodeItem? _selectedTreeNode;

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

  // Путь к открытому «большому» ZIP (обзор без загрузки в память); null — открыт не потоковый ZIP.
  // Если задан — извлечение идёт потоковым ZIP-путём из файла.
  private string? _zipArchivePath;

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
  private bool _isEditingPath;
  private string _editablePath = string.Empty;
  private bool _isBusy;
  private bool _isOperating;
  private double _progressPercent;
  private string? _progressText;
  private string? _progressEta;
  private bool _isScanning;
  private string? _scanStatus;
  private string? _currentFileStatus;
  private bool _isOperationWindowActive;
  private bool _isOpening;

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
    NavigateToCrumbCommand = new RelayCommand<PathCrumb>(NavigateToCrumb);
    CommitPathCommand = new RelayCommand(CommitPath);
    CancelEditPathCommand = new RelayCommand(CancelEditPath);
    ExtractAllCommand = new AsyncRelayCommand(ExtractAllAsync, () => HasArchive && !IsOperating, this);
    ExtractSelectedCommand = new AsyncRelayCommand(ExtractSelectedAsync, () => CanExtractSelected && !IsOperating, this);
    ExtractArchiveFileCommand = new AsyncRelayCommand(ExtractArchiveFileAsync, () => !IsOperating, this);
    CreateCommand = new AsyncRelayCommand(CreateFromFilesAsync, () => CanCreate && !IsOperating, this);
    CreateFromFolderCommand = new AsyncRelayCommand(CreateFromFolderAsync, () => CanCreateFromFolder && !IsOperating, this);
    CreateFromSelectionCommand = new AsyncRelayCommand(CreateFromSelectionAsync, () => CanCreateFromSelection && !IsOperating, this);
    CancelCommand = new RelayCommand(Cancel, () => IsOperating || IsScanning, this);

    // На старте (если шов ФС внедрён) показываем дерево файловой системы от корней-дисков.
    if (_fileSystemBrowser is not null)
      ShowFileSystemTree();
  }

  /// <summary>Заголовок окна: базовый либо «имя_архива — LzmaSharp» при открытом архиве.</summary>
  public string Title
  {
    get => _title;
    set
    {
      // Пути открытия задают Title ПОСЛЕ RefreshView, поэтому корневая крошка архива
      // (имя архива) выводится из заголовка — пересобираем её при смене Title.
      if (Set(ref _title, value) && HasArchive)
        SetBreadcrumbs(BuildArchiveCrumbs());
    }
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
        OnPropertyChanged(nameof(CanExtractSelected));
        OnPropertyChanged(nameof(CanEditPath));
        IsEditingPath = false; // смена режима гасит ввод пути
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

  /// <summary>Активен ли режим ввода пути в адресной строке (текстовое поле вместо крошек).</summary>
  public bool IsEditingPath
  {
    get => _isEditingPath;
    private set => Set(ref _isEditingPath, value);
  }

  /// <summary>Редактируемый текст адресной строки (полный путь для ввода).</summary>
  public string EditablePath
  {
    get => _editablePath;
    set => Set(ref _editablePath, value);
  }

  /// <summary>Можно ли редактировать адрес (ввод пути доступен только в режиме браузера ФС).</summary>
  public bool CanEditPath => IsFileSystemMode;

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
  /// Активно ли модальное окно операции (создание/извлечение). Пока оно открыто, прогресс
  /// показывается в НЁМ, а нижняя панель главного окна скрывается, чтобы не дублировать полосу.
  /// </summary>
  public bool IsOperationWindowActive
  {
    get => _isOperationWindowActive;
    set
    {
      if (Set(ref _isOperationWindowActive, value))
        OnPropertyChanged(nameof(IsBottomBarVisible));
    }
  }

  /// <summary>
  /// Идёт ли открытие/обзор архива. Показывает индикатор занятости сразу (в отличие от отложенного
  /// <see cref="IsBusy"/>), чтобы открытие не выглядело «зависанием».
  /// </summary>
  public bool IsOpening
  {
    get => _isOpening;
    private set
    {
      if (Set(ref _isOpening, value))
        OnPropertyChanged(nameof(IsBottomBarVisible));
    }
  }

  /// <summary>
  /// Видима ли нижняя панель главного окна: во время операции/сканирования/открытия (строка прогресса)
  /// либо когда есть статусное сообщение — но НЕ когда открыто модальное окно операции (там свой прогресс).
  /// Пусто — панель скрыта, чтобы не занимать место.
  /// </summary>
  public bool IsBottomBarVisible =>
      !IsOperationWindowActive && (IsCancelVisible || IsOpening || !string.IsNullOrEmpty(StatusMessage));

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

  /// <summary>Содержимое текущей папки архива (плоский список, режим архива).</summary>
  public ObservableCollection<ArchiveItem> Items { get; } = [];

  /// <summary>Дерево файловой системы (корни-диски → лениво вглубь), режим браузера ФС.</summary>
  public ObservableCollection<TreeNodeItem> FileSystemTree { get; } = [];

  /// <summary>Выделенный узел дерева ФС (для подсветки/скролла при переходе по адресу).</summary>
  public TreeNodeItem? SelectedTreeNode
  {
    get => _selectedTreeNode;
    set => Set(ref _selectedTreeNode, value);
  }

  /// <summary>«Хлебные крошки» текущего пути (корень → текущая папка). Каждая крошка кликабельна.</summary>
  public ObservableCollection<PathCrumb> Breadcrumbs { get; } = [];

  /// <summary>Команда «Открыть архив…».</summary>
  public AsyncRelayCommand OpenCommand { get; }

  /// <summary>Команда «Вверх» (на уровень выше по дереву архива).</summary>
  public RelayCommand NavigateUpCommand { get; }

  /// <summary>Команда перехода к сегменту «хлебных крошек» (клик по крошке пути).</summary>
  public RelayCommand<PathCrumb> NavigateToCrumbCommand { get; }

  /// <summary>Команда применить введённый путь адресной строки (Enter).</summary>
  public RelayCommand CommitPathCommand { get; }

  /// <summary>Команда отменить ввод пути адресной строки (Esc / потеря фокуса).</summary>
  public RelayCommand CancelEditPathCommand { get; }

  /// <summary>
  /// Команда «Открыть большой архив…» — обзор содержимого .7z по пути БЕЗ загрузки в память
  /// (для архивов больше 2 ГиБ). Извлечение потом идёт потоковым путём из файла.
  /// </summary>
  public AsyncRelayCommand OpenArchiveFileCommand { get; }

  /// <summary>Команда «Извлечь всё» — распаковать содержимое архива в выбранную папку.</summary>
  public AsyncRelayCommand ExtractAllCommand { get; }

  /// <summary>Команда «Извлечь выбранное» — распаковать только отмеченные записи открытого архива.</summary>
  public AsyncRelayCommand ExtractSelectedCommand { get; }

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

  private bool _useZipFormat;

  /// <summary>
  /// Создавать ZIP (Store/Deflate) вместо 7z. Настройки 7z (метод/потоки/словарь/тома/AES) при этом
  /// не применяются — ZIP выбирает Store/Deflate пофайлово сам.
  /// </summary>
  public bool UseZipFormat
  {
    get => _useZipFormat;
    set => Set(ref _useZipFormat, value);
  }

  private bool _encryptZip;

  /// <summary>Шифровать создаваемый ZIP паролем (WinZip-AES, AES-256). Применяется только при <see cref="UseZipFormat"/>.</summary>
  public bool EncryptZip
  {
    get => _encryptZip;
    set => Set(ref _encryptZip, value);
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

  /// <summary>Доступно ли извлечение выбранного (открыт архив и есть отмеченные записи).</summary>
  public bool CanExtractSelected => HasArchive && HasSelection;

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
        await OpenArchiveFromBrowserAsync(item.Name, item.FullPath, item.Size);

      return; // прочие файлы двойным кликом пока не открываем
    }

    NavigateInto(item);
  }

  /// <summary>Двойной клик по узлу дерева ФС: файл-архив — открыть; папку раскрывает сам TreeView.</summary>
  public async Task ActivateTreeNodeAsync(TreeNodeItem? node)
  {
    if (node is null || node.IsDirectory)
      return;

    if (node.IsArchiveFile && node.FullPath is not null)
      await OpenArchiveFromBrowserAsync(node.Name, node.FullPath, node.Size);
  }

  // Открывает архив по пути из браузера ФС (читает в память ≤2 ГиБ, дальше общий путь обработки).
  private async Task OpenArchiveFromBrowserAsync(string name, string path, long size)
  {
    if (_fileSystemBrowser is null)
      return;

    if (size > int.MaxValue)
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

    await RunOpenAsync(() => ProcessOpenedArchiveAsync(new PickedArchive(name, bytes)));
  }

  /// <summary>Войти в элемент: для папки — перейти внутрь; файлы пока игнорируются.</summary>
  public void NavigateInto(ArchiveItem item)
  {
    if (item is null)
      return;

    // В режиме браузера ФС «захода внутрь» нет — там дерево (раскрытие узлов). Метод — только для архива.
    if (IsFileSystemMode)
      return;

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
      return; // в ФС-дереве «Вверх» не нужен (одно дерево от дисков)

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

  // Строит дерево ФС от корней-дисков (вглубь — лениво при раскрытии). Одно дерево, без «текущей папки»:
  // навигация «заход/крошки/Вверх» в ФС не нужна (раскрываем узлы). Режим архива остаётся плоским (Items).
  private void ShowFileSystemTree()
  {
    if (_fileSystemBrowser is null)
      return;

    // Отписываемся от старых узлов и очищаем дерево/список.
    foreach (TreeNodeItem node in _trackedTreeNodes)
      node.PropertyChanged -= OnTreeNodeChanged;
    _trackedTreeNodes.Clear();
    FileSystemTree.Clear();
    ClearItems(); // в ФС Items не используем

    foreach (FileSystemEntry root in _fileSystemBrowser.ListRoots()
                 .OrderByDescending(e => e.IsDirectory)
                 .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
    {
      FileSystemTree.Add(MakeFsNode(root));
    }

    SelectedCount = 0;
    _currentDirectory = null;
    CurrentPath = "Этот компьютер";
    CanGoUp = false;
    SetBreadcrumbs([]); // в ФС-дереве крошек нет
  }

  // Создаёт узел дерева ФС с ленивым загрузчиком детей + подпиской на изменение выбора.
  private TreeNodeItem MakeFsNode(FileSystemEntry e)
  {
    var node = new TreeNodeItem(LoadFsChildren)
    {
      Name = e.Name,
      IsDirectory = e.IsDirectory,
      Size = e.Size,
      FullPath = e.FullPath,
    };
    node.AddLoadingPlaceholder();
    node.PropertyChanged += OnTreeNodeChanged;
    _trackedTreeNodes.Add(node);
    return node;
  }

  // Ленивая догрузка детей узла ФС (папки первыми, затем по имени).
  private IReadOnlyList<TreeNodeItem> LoadFsChildren(TreeNodeItem parent)
  {
    if (_fileSystemBrowser is null || parent.FullPath is null)
      return [];

    return [.. _fileSystemBrowser.ListDirectory(parent.FullPath)
        .OrderByDescending(e => e.IsDirectory)
        .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
        .Select(MakeFsNode)];
  }

  // Изменилась галочка узла дерева → пересчитываем выбор.
  private void OnTreeNodeChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(TreeNodeItem.IsSelected))
      SelectedCount = CountSelectedTreeNodes(FileSystemTree);
  }

  private static int CountSelectedTreeNodes(IEnumerable<TreeNodeItem> nodes)
  {
    int count = 0;
    foreach (TreeNodeItem node in nodes)
    {
      if (node.IsSelected)
        count++;
      count += CountSelectedTreeNodes(node.Children);
    }

    return count;
  }

  // Раскрывает дерево ФС до указанного пути (адресная строка): раскрывает предков, чтобы папка стала
  // видимой. Возвращает найденный узел или null. Сопоставление по FullPath.
  private TreeNodeItem? ExpandToPath(string canonicalPath)
  {
    IEnumerable<TreeNodeItem> level = FileSystemTree;

    while (true)
    {
      TreeNodeItem? node = null;
      foreach (TreeNodeItem candidate in level)
      {
        if (candidate.FullPath is not { } fp)
          continue;

        if (PathEquals(fp, canonicalPath))
          return node = candidate; // точное совпадение

        if (IsUnder(canonicalPath, fp))
        {
          node = candidate;
          break;
        }
      }

      if (node is null)
        return null;

      node.IsExpanded = true; // догружает детей
      level = node.Children;
    }
  }

  private static bool PathEquals(string a, string b)
      => string.Equals(a.TrimEnd('\\', '/'), b.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

  // target лежит ВНУТРИ каталога dir (dir — префикс-путь).
  private static bool IsUnder(string target, string dir)
  {
    string d = dir.TrimEnd('\\', '/');
    string t = target.TrimEnd('\\', '/');
    return t.Length > d.Length
        && t.StartsWith(d, StringComparison.OrdinalIgnoreCase)
        && (t[d.Length] == '\\' || t[d.Length] == '/');
  }

  /// <summary>Переход к сегменту «хлебных крошек» (в ФС — по пути, в архиве — по глубине узла).</summary>
  public void NavigateToCrumb(PathCrumb? crumb)
  {
    if (crumb is null || crumb.IsCurrent)
      return;

    if (IsFileSystemMode)
      return; // в ФС-дереве крошек нет

    // Режим архива: спускаемся от корня к узлу нужной глубины по текущей цепочке предков.
    var chain = new List<Node>();
    for (Node? n = _current; n is not null; n = n.Parent)
      chain.Add(n);
    chain.Reverse(); // chain[0] — корень, chain[^1] — текущий узел

    if (crumb.Depth < 0 || crumb.Depth >= chain.Count)
      return;

    _current = chain[crumb.Depth];
    RefreshView();
  }

  /// <summary>Включает режим ввода пути (адресная строка → текстовое поле). Только в режиме ФС.</summary>
  public void BeginEditPath()
  {
    if (!CanEditPath)
      return;

    EditablePath = _currentDirectory ?? string.Empty;
    IsEditingPath = true;
  }

  /// <summary>Отменяет ввод пути (вернуться к крошкам без перехода).</summary>
  public void CancelEditPath() => IsEditingPath = false;

  /// <summary>Применяет введённый путь (Enter): переходит по нему, если это существующая папка.</summary>
  public void CommitPath() => NavigateToPath(EditablePath);

  /// <summary>
  /// Переход по введённому пути в режиме браузера ФС (как адресная строка проводника): пустой ввод —
  /// к списку корней; существующая папка — переход в неё (ввод-режим гаснет); иначе — статус-сообщение,
  /// поле остаётся открытым для правки.
  /// </summary>
  public void NavigateToPath(string? raw)
  {
    if (_fileSystemBrowser is null || !IsFileSystemMode)
      return;

    string input = (raw ?? string.Empty).Trim().Trim('"');

    if (input.Length == 0)
    {
      IsEditingPath = false; // пусто → просто закрыть ввод (дерево уже от корней)
      return;
    }

    string? resolved = _fileSystemBrowser.ResolveDirectory(input);
    if (resolved is not null)
    {
      // Раскрываем дерево до пути (адрес → показать папку), выделяем найденный узел.
      TreeNodeItem? node = ExpandToPath(resolved);
      if (node is not null)
        SelectedTreeNode = node;
      IsEditingPath = false;
      return;
    }

    StatusMessage = $"Путь не найден или это не папка: {input}";
  }

  // Пересобирает коллекцию крошек (ссылка на коллекцию стабильна — важно для привязки XAML).
  private void SetBreadcrumbs(IReadOnlyList<PathCrumb> crumbs)
  {
    Breadcrumbs.Clear();
    foreach (PathCrumb crumb in crumbs)
      Breadcrumbs.Add(crumb);
  }

  // Крошки пути каталога ФС: «Этот компьютер» → диск → папки. Чистая (тестируется без I/O).
  internal static IReadOnlyList<PathCrumb> BuildFileSystemCrumbs(string? directory)
  {
    if (directory is null)
      return [new PathCrumb { Name = "Этот компьютер", FullPath = null, IsCurrent = true }];

    var crumbs = new List<PathCrumb> { new() { Name = "Этот компьютер", FullPath = null } };

    string root = Path.GetPathRoot(directory) ?? string.Empty;
    if (root.Length > 0)
    {
      string acc = root;
      crumbs.Add(new PathCrumb { Name = TrimTrailingSeparators(root), FullPath = acc });

      string remainder = directory.Length > root.Length ? directory[root.Length..] : string.Empty;
      foreach (string part in remainder.Split(
                   [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                   StringSplitOptions.RemoveEmptyEntries))
      {
        acc = Path.Combine(acc, part);
        crumbs.Add(new PathCrumb { Name = part, FullPath = acc });
      }
    }
    else
    {
      // Нет корня (относительный/необычный путь) — показываем путь одним сегментом.
      crumbs.Add(new PathCrumb { Name = directory, FullPath = directory });
    }

    // Последняя крошка — текущая (не кликается).
    PathCrumb last = crumbs[^1];
    crumbs[^1] = new PathCrumb { Name = last.Name, FullPath = last.FullPath, IsCurrent = true };
    return crumbs;
  }

  private static string TrimTrailingSeparators(string path)
  {
    string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return trimmed.Length == 0 ? path : trimmed;
  }

  // Крошки пути внутри открытого архива: имя архива (корень) → вложенные папки.
  private IReadOnlyList<PathCrumb> BuildArchiveCrumbs()
  {
    var chain = new List<Node>();
    for (Node? n = _current; n is not null; n = n.Parent)
      chain.Add(n);
    chain.Reverse(); // chain[0] — корень архива

    const string suffix = " — LzmaSharp";
    string rootName = Title.EndsWith(suffix) && Title.Length > suffix.Length
        ? Title[..^suffix.Length]
        : "Архив";

    var crumbs = new List<PathCrumb>(chain.Count);
    for (int i = 0; i < chain.Count; i++)
    {
      crumbs.Add(new PathCrumb
      {
        Name = i == 0 ? rootName : chain[i].Name,
        Depth = i,
        IsCurrent = i == chain.Count - 1,
      });
    }

    return crumbs;
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
    // Объединённое открытие: одна кнопка «Открыть…» сама решает по размеру, читать в память или
    // открывать потоково. Пикеры без поддержки (фейки в тестах) идут прежним байтовым путём.
    if (_picker.SupportsUnifiedOpen)
    {
      PickedOpenTarget? target = await _picker.PickForOpenAsync();

      if (target is null)
        return; // выбор отменён

      await RunOpenAsync(() => OpenTargetAsync(target));
      return;
    }

    PickedArchive? picked = await _picker.PickAsync();

    if (picked is null)
      return; // выбор отменён — состояние не трогаем

    await RunOpenAsync(() => ProcessOpenedArchiveAsync(picked));
  }

  // Открывает выбранную цель: нелокальный источник (байты) — in-memory; ZIP по пути — всегда потоково
  // (Store/Deflate + ZIP64, любой размер); 7z ≤ 2 ГиБ — in-memory (пароль/шифрование/все формы),
  // больше — потоково.
  private async Task OpenTargetAsync(PickedOpenTarget target)
  {
    if (target.Bytes is { } inlineBytes)
    {
      await ProcessOpenedArchiveAsync(new PickedArchive(target.Name, inlineBytes));
      return;
    }

    if (target.LocalPath is not { } path)
    {
      ResetTree();
      StatusMessage = "Не удалось открыть файл архива.";
      return;
    }

    // 7z ≤ 2 ГиБ — полный in-memory путь. ZIP и всё, что больше 2 ГиБ, — потоковый обзор.
    if (target.Length <= int.MaxValue && !await _archiveService.IsZipFileAsync(path))
    {
      byte[]? bytes = await _archiveService.ReadFileBytesAsync(path);

      if (bytes is null)
      {
        ResetTree();
        StatusMessage = "Не удалось прочитать файл архива.";
        return;
      }

      await ProcessOpenedArchiveAsync(new PickedArchive(target.Name, bytes));
      return;
    }

    await OpenArchiveStreamingAsync(path);
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
      _zipArchivePath = null;
      StatusMessage = outcome.Entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();
    StatusMessage = outcome.Result == ZipReadResult.NotSupported
        ? "ZIP использует шифрование или метод сжатия, кроме Store/Deflate — такой архив можно открыть в 7-Zip."
        : "Не удалось открыть ZIP: файл повреждён или не является поддерживаемым ZIP-архивом.";
  }

  // Обзор БОЛЬШОГО архива по пути: читаем только листинг (без распаковки и без загрузки в память).
  private async Task OpenArchiveFileAsync()
  {
    string? archivePath = await _picker.PickArchivePathAsync();

    if (archivePath is null)
      return; // выбор отменён / нет локального пути

    await RunOpenAsync(() => OpenArchiveStreamingAsync(archivePath));
  }

  // Потоковый обзор архива по пути (ZIP → каталог ZIP64; иначе 7z-листинг). Без загрузки в память.
  private async Task OpenArchiveStreamingAsync(string archivePath)
  {
    // ZIP → потоковый обзор каталога (поддержка ZIP >4 ГиБ / ZIP64).
    if (await _archiveService.IsZipFileAsync(archivePath))
    {
      await OpenZipStreamingAsync(archivePath);
      return;
    }

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
      _zipArchivePath = null;
      StatusMessage = outcome.Entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();
    StatusMessage = outcome.Result == SevenZipArchiveDecodeResult.NotSupported
        ? "Этот архив нельзя открыть потоково (например, шифрование, закодированный заголовок или "
          + "сложные фильтры). Небольшой архив попробуйте открыть без потока."
        : "Не удалось открыть архив: файл повреждён или не является поддерживаемым 7z-архивом.";
  }

  // Потоковый обзор БОЛЬШОГО ZIP по пути: читаем только каталог (без распаковки и без загрузки в память).
  private async Task OpenZipStreamingAsync(string archivePath)
  {
    ZipListOutcome outcome = await _archiveService.OpenZipFromFileAsync(archivePath);

    if (outcome.Result == ZipReadResult.Ok)
    {
      _root = BuildTree(outcome.Entries);
      _current = _root;
      RefreshView();

      HasArchive = true;
      Title = $"{System.IO.Path.GetFileName(archivePath)} — LzmaSharp";
      _archiveBytes = null;
      _archivePassword = null;
      _archivePath = null;
      _zipEntries = null;
      _zipArchivePath = archivePath; // источник для потокового ZIP-извлечения
      StatusMessage = outcome.Entries.Length == 0 ? "Архив пуст." : null;
      return;
    }

    ResetTree();
    StatusMessage = outcome.Result == ZipReadResult.NotSupported
        ? "Этот ZIP нельзя открыть потоково (шифрование или метод сжатия, кроме Store/Deflate)."
        : "Не удалось открыть ZIP: файл повреждён или не является поддерживаемым ZIP-архивом.";
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
    _zipArchivePath = null;
  }

  // Извлечение содержимого открытого архива в выбранную папку.
  private async Task ExtractAllAsync()
  {
    // Открыт «большой» ZIP по пути — потоковая распаковка прямо из файла.
    if (_zipArchivePath is { } zipArchivePath)
    {
      await ExtractZipStreamingAsync(zipArchivePath);
      return;
    }

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

  // Извлечение ТОЛЬКО отмеченных записей открытого архива. Предикат по имени строится из выбора
  // текущей папки (файл → точный путь, папка → всё поддерево); маршрутизация по режиму открытия
  // повторяет ExtractAllAsync. Для 7z solid-folder декодируется целиком, но на диск идут только
  // выбранные подпотоки (фильтр в ядре).
  private async Task ExtractSelectedAsync()
  {
    Func<string, bool>? predicate = BuildArchiveExtractPredicate();
    if (predicate is null)
      return; // ничего не отмечено (гарантируется CanExtractSelected, но перестрахуемся)

    string? destination = await _folderPicker.PickFolderAsync();
    if (destination is null)
      return; // выбор папки отменён

    IProgress<SevenZipProgress> progress = CreateProgress();
    var currentFile = new Progress<string>(name => CurrentFileStatus = FormatExtractingFileStatus(name));

    // Открыт «большой» ZIP по пути — потоковое частичное извлечение.
    if (_zipArchivePath is { } zipArchivePath)
    {
      string? zipPassword = null;
      if (await _archiveService.IsZipEncryptedAsync(zipArchivePath))
      {
        zipPassword = await _passwordPrompt.RequestAsync(Path.GetFileName(zipArchivePath), previousAttemptFailed: false);
        if (zipPassword is null)
        {
          StatusMessage = "Извлечение отменено: для зашифрованного архива нужен пароль.";
          return;
        }
      }

      await RunOperationAsync(async token =>
      {
        try
        {
          ZipExtractResult result = await _archiveService.ExtractSelectedZipFileAsync(
              zipArchivePath, destination, predicate, token, currentFile, progress, zipPassword);
          StatusMessage = result == ZipExtractResult.WrongPassword
              ? "Неверный пароль для зашифрованного ZIP."
              : ZipExtractStatus(result, destination);
        }
        finally { CurrentFileStatus = null; }
      });
      return;
    }

    // Открыт ZIP in-memory — фильтруем уже прочитанные записи (члены независимы).
    if (_zipEntries is { } zipEntries)
    {
      ZipEntry[] subset = [.. zipEntries.Where(e => predicate(e.Name))];
      await RunOperationAsync(async token =>
      {
        try
        {
          ZipExtractResult result = await _archiveService.ExtractZipAsync(subset, destination, token, currentFile);
          StatusMessage = ZipExtractStatus(result, destination);
        }
        finally { CurrentFileStatus = null; }
      });
      return;
    }

    // Открыт как «большой» (потоковый) 7z — частичное извлечение прямо из файла.
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
          SevenZipArchiveDecodeResult result = await _archiveService.ExtractSelectedArchiveFileAsync(
              archivePath, destination, predicate, progress, token, currentFile, streamPassword);
          StatusMessage = StreamingExtractStatus(result, destination, encrypted);
        }
        finally { CurrentFileStatus = null; }
      });
      return;
    }

    // In-memory 7z.
    if (_archiveBytes is { } bytes)
    {
      string? password = _archivePassword;
      await RunOperationAsync(async token =>
      {
        try
        {
          SevenZipArchiveDecodeResult result = await _archiveService.ExtractSelectedAsync(
              bytes, password, destination, predicate, progress, token, currentFile);
          StatusMessage = ExtractStatus(result, destination);
        }
        finally { CurrentFileStatus = null; }
      });
    }
  }

  // Предикат «извлекать ли запись архива» по отмеченным элементам ТЕКУЩЕЙ папки: файл — точный путь
  // внутри архива, папка — весь её поддерев (префикс «путь/»). Имена нормализуем к '/'. null — если
  // ничего не отмечено.
  private Func<string, bool>? BuildArchiveExtractPredicate()
  {
    string prefix = BuildCurrentPath();
    string basePath = prefix.Length == 0 ? string.Empty : prefix + "/";

    var files = new HashSet<string>(StringComparer.Ordinal);
    var folderPrefixes = new List<string>();

    foreach (ArchiveItem item in Items)
    {
      if (!item.IsSelected)
        continue;

      string full = basePath + item.Name;
      if (item.IsDirectory)
        folderPrefixes.Add(full + "/");
      else
        files.Add(full);
    }

    if (files.Count == 0 && folderPrefixes.Count == 0)
      return null;

    return name =>
    {
      string norm = name.Replace('\\', '/');
      if (files.Contains(norm))
        return true;

      foreach (string p in folderPrefixes)
        if (norm == p[..^1] || norm.StartsWith(p, StringComparison.Ordinal))
          return true;

      return false;
    };
  }

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

  // Потоковое извлечение «большого» ZIP по пути в папку (без загрузки в память). Если папка не задана —
  // спрашиваем её (вызов из «Извлечь всё»); из «Извлечь архив с диска…» папка уже выбрана.
  private async Task ExtractZipStreamingAsync(string archivePath, string? destination = null)
  {
    destination ??= await _folderPicker.PickFolderAsync();

    if (destination is null)
      return; // выбор папки отменён

    // Зашифрованный ZIP (WinZip-AES) — спрашиваем пароль ДО начала; отмена — не извлекаем.
    string? password = null;
    if (await _archiveService.IsZipEncryptedAsync(archivePath))
    {
      password = await _passwordPrompt.RequestAsync(Path.GetFileName(archivePath), previousAttemptFailed: false);
      if (password is null)
      {
        StatusMessage = "Извлечение отменено: для зашифрованного архива нужен пароль.";
        return;
      }
    }

    IProgress<SevenZipProgress> progress = CreateProgress();
    var currentFile = new Progress<string>(name => CurrentFileStatus = FormatExtractingFileStatus(name));

    await RunOperationAsync(async token =>
    {
      try
      {
        ZipExtractResult result = await _archiveService.ExtractZipFileAsync(archivePath, destination, token, currentFile, progress, password);
        StatusMessage = result == ZipExtractResult.WrongPassword
            ? "Неверный пароль для зашифрованного ZIP."
            : ZipExtractStatus(result, destination);
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

    // ZIP → потоковое ZIP-извлечение по пути (без пароля/шифрования — ZIP-шифрование не поддержано).
    if (await _archiveService.IsZipFileAsync(archivePath))
    {
      await ExtractZipStreamingAsync(archivePath, destination);
      return;
    }

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

    await CreateStreamingFromSourceAsync(async (scanProgress, token) =>
    {
      // Тяжёлый обход диска — вне UI-потока. А сбор ссылок и отчёты прогресса — ПОСЛЕ await, на
      // UI-потоке (await без ConfigureAwait возобновляется в UI-контексте): установка IsScanning
      // дёргает CanExecuteChanged у кнопок Avalonia, что из фонового потока падает VerifyAccess.
      IReadOnlyList<ArchiveSourceFile> sources =
          await Task.Run(() => _fileSystemBrowser.EnumerateForArchive(paths), token);

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

      return (IReadOnlyList<PickedFileRef>?)refs;
    });
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

    string? path = await _saveFilePicker.PickSavePathAsync(UseZipFormat ? "archive.zip" : "archive.7z");

    if (path is null)
      return;

    // ZIP-формат: свой потоковый writer (Store/Deflate), настройки 7z не применяются.
    if (UseZipFormat)
    {
      await CreateZipStreamingAsync(files, path);
      return;
    }

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

  // Потоковое создание ZIP из отсканированных ссылок на файлы (Store/Deflate, ZIP64 при переполнении).
  private async Task CreateZipStreamingAsync(IReadOnlyList<PickedFileRef> files, string path)
  {
    // Шифрование ZIP (WinZip-AES): спрашиваем пароль ДО начала операции; отмена — не создаём.
    string? password = null;
    if (EncryptZip)
    {
      password = _createPasswordPrompt is null ? null : await _createPasswordPrompt.RequestNewPasswordAsync();
      if (password is null)
      {
        StatusMessage = "Создание отменено: для шифрования нужен пароль.";
        return;
      }
    }

    IProgress<SevenZipProgress> progress = CreateProgress();
    var currentFile = new Progress<string>(name => CurrentFileStatus = $"Сжатие: {name}");

    await RunOperationAsync(async token =>
    {
      var entries = new List<ZipStreamingEntry>(files.Count);
      long originalBytes = 0;

      foreach (PickedFileRef file in files)
      {
        entries.Add(new ZipStreamingEntry(file.Name, file.Length, file.OpenRead));
        originalBytes += file.Length;
      }

      ZipWriteResult result;
      try
      {
        result = await _archiveService.CreateZipToFileAsync(entries, path, progress, token, currentFile, password);
      }
      finally
      {
        CurrentFileStatus = null;
      }

      if (result != ZipWriteResult.Ok)
      {
        StatusMessage = result == ZipWriteResult.NotSupported
            ? "ZIP: отдельный файл больше 2 ГиБ пока не поддержан — используйте 7z."
            : "Не удалось создать ZIP: ошибка ввода-вывода или некорректный набор файлов.";
        return;
      }

      long compressedBytes = 0;
      try { compressedBytes = new FileInfo(path).Length; }
      catch (IOException) { }

      StatusMessage = FormatCreateSummary(path, originalBytes, compressedBytes);
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

  // Обёртка открытия/обзора архива: сразу показывает индикатор занятости (открытие не отменяемо,
  // отдельный от RunOperationAsync путь) и гасит его в finally. Само сообщение о результате ставит body.
  private async Task RunOpenAsync(Func<Task> open)
  {
    IsOpening = true;
    StatusMessage = null; // прошлый статус убираем; текст «Открываю архив…» показывает XAML по IsOpening

    try
    {
      await open();
    }
    finally
    {
      IsOpening = false;
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

  private static Node BuildTree(IEnumerable<ZipStreamEntry> entries)
      => BuildTreeCore(entries.Select(e => (e.Name, e.IsDirectory, e.UncompressedSize)));

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
    _zipArchivePath = null;

    // Если доступен браузер ФС — возвращаемся к дереву файловой системы, а не к пустому состоянию.
    if (_fileSystemBrowser is not null)
      ShowFileSystemTree();
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
    SetBreadcrumbs(BuildArchiveCrumbs());
  }

  /// <summary>Число отмеченных галочкой элементов текущего списка.</summary>
  public int SelectedCount
  {
    get => _selectedCount;
    private set
    {
      if (Set(ref _selectedCount, value))
      {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(CanExtractSelected));
      }
    }
  }

  /// <summary>Есть ли отмеченные элементы.</summary>
  public bool HasSelection => SelectedCount > 0;

  /// <summary>Полные пути отмеченных узлов дерева ФС (папки и файлы) — для действий над выбором.
  /// Отмеченная папка покрывает всё поддерево, поэтому в неё не спускаемся.</summary>
  public IReadOnlyList<string> SelectedPaths
  {
    get
    {
      var result = new List<string>();
      CollectSelectedPaths(FileSystemTree, result);
      return result;
    }
  }

  private static void CollectSelectedPaths(IEnumerable<TreeNodeItem> nodes, List<string> result)
  {
    foreach (TreeNodeItem node in nodes)
    {
      if (node.IsSelected)
      {
        if (node.FullPath is not null)
          result.Add(node.FullPath);
        // выбранная папка покрывает поддерево — глубже не идём
      }
      else
      {
        CollectSelectedPaths(node.Children, result);
      }
    }
  }

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
