using System.Security.Cryptography;
using System.Text;

namespace Lzma.Core;

/// <summary>
/// Вид записи в журнале staged-извлечения.
/// </summary>
internal enum StagedEntryKind
{
  /// <summary>Файл создан заново (прежнего не было).</summary>
  CreatedNew,

  /// <summary>Файл заменил прежний (существующий был перемещён в .bak).</summary>
  Replaced,

  /// <summary>Каталог создан операцией извлечения.</summary>
  CreatedDir,

  /// <summary>Атрибуты файла были изменены (fallback-сценарий).</summary>
  AttributesChanged,
}

/// <summary>
/// <para>Журнал staged-извлечения: управляет временными путями, публикацией и откатом.
/// Не знает про декодирование, CRC, пароли и форматы архивов — только файловая механика.</para>
/// <para>Lifetime — одна операция извлечения (one-shot, не thread-safe).</para>
/// <para>
/// Формат имён стадии: <c>{fullPath}.{16 hex}.tmp</c>, backup: <c>{fullPath}.{16 hex}.bak</c>.
/// Суффикс генерируется в том же каталоге, что и конечный путь (тот же том ⇒ rename без копирования).
/// </para>
/// </summary>
internal sealed class StagedExtractionJournal
{
  // Записи о файлах (в порядке публикации).
  private readonly List<StagedFileEntry> _files = new();

  // Записи о каталогах (в порядке создания).
  private readonly List<string> _dirs = new();

  // Записи об изменённых атрибутах (fallback).
  private readonly List<AttributeEntry> _attrChanges = new();

  // Флаг точки фиксации: после Commit откат не выполняется (one-shot).
  private bool _committed;

  // Флаг выполненного отката: повторный вызов Rollback не должен удалять уже восстановленные
  // прежние байты (идемпотентность по последствию).
  private bool _rolledBack;

  /// <summary>
  /// Создаёт staged-путь РЯДОМ с конечным (тот же каталог ⇒ тот же том ⇒ rename без копирования).
  /// Формат: <c>{fullPath}.{16 hex}.tmp</c>. При коллизии — повторная генерация.
  /// </summary>
  /// <param name="fullPath">Конечный путь файла.</param>
  /// <returns>Путь для staged-записи (временный файл).</returns>
  public string CreateStagedPath(string fullPath)
  {
    for (int attempt = 0; attempt < 16; attempt++)
    {
      string suffix = GenerateHexSuffix();
      string candidate = fullPath + "." + suffix + ".tmp";
      if (!FileExists(candidate))
        return candidate;
    }

    // Все попытки дали занятое имя: возвращать существующий путь НЕЛЬЗЯ — FileMode.Create
    // обрезал бы чужой файл. Отказываем явно (граница ExtractCore смаппит в IOError).
    throw new IOException($"Не удалось подобрать уникальное имя staged-файла для \"{fullPath}\".");
  }

  /// <summary>
  /// Регистрирует созданный каталог для отката в обратном порядке.
  /// </summary>
  /// <param name="directory">Путь созданного каталога.</param>
  public void TrackCreatedDirectory(string directory)
  {
    _dirs.Add(directory);
  }

  /// <summary>
  /// Определяет вид записи перед публикацией и подбирает свободное имя backup.
  /// Если конечный файл существует — <see cref="StagedEntryKind.Replaced"/> (нужен backup),
  /// иначе <see cref="StagedEntryKind.CreatedNew"/>. Метод приватный: единственный вызывающий —
  /// <see cref="Publish"/>, что исключает двойную регистрацию одного элемента.
  /// </summary>
  /// <param name="fullPath">Конечный путь файла.</param>
  /// <param name="backupPath">Свободный путь backup (для <see cref="StagedEntryKind.Replaced"/>) или null.</param>
  /// <returns>Вид записи.</returns>
  private StagedEntryKind RegisterTarget(string fullPath, out string? backupPath)
  {
    if (!FileExists(fullPath))
    {
      backupPath = null;
      return StagedEntryKind.CreatedNew;
    }

    // Имя backup подбирается так же, как имя стадии: занятое имя привело бы к отказу File.Move
    // и лишнему откату всей операции.
    for (int attempt = 0; attempt < 16; attempt++)
    {
      string candidate = fullPath + "." + GenerateHexSuffix() + ".bak";
      if (!FileExists(candidate))
      {
        backupPath = candidate;
        return StagedEntryKind.Replaced;
      }
    }

    throw new IOException($"Не удалось подобрать уникальное имя backup-файла для \"{fullPath}\".");
  }

  /// <summary>
  /// Публикует ОДИН элемент: сначала перемещает прежний файл в backup (если есть),
  /// затем перемещает staged-файл на конечный путь.
  /// Вызывается ТОЛЬКО после успешной записи и Dispose потока стадии.
  /// </summary>
  /// <param name="stagedPath">Путь staged-файла (временный).</param>
  /// <param name="fullPath">Конечный путь публикации.</param>
  public void Publish(string stagedPath, string fullPath)
  {
    StagedEntryKind kind = RegisterTarget(fullPath, out string? backupPath);

    // Запись добавляется в журнал ДО переносов: если второй перенос откажет, прежний файл уже
    // уехал в .bak, и откат обязан вернуть его на место (иначе файл исчезнет из своего имени).
    var entry = new StagedFileEntry(kind, fullPath, stagedPath, backupPath);
    _files.Add(entry);

    if (kind == StagedEntryKind.Replaced && backupPath is not null)
    {
      // Переносим прежний файл в backup (атрибуты, включая ReadOnly, переносятся вместе с файлом).
      MoveFile(fullPath, backupPath);
    }

    // Переносим staged на конечный путь.
    MoveFile(stagedPath, fullPath);

    entry.Published = true;
  }

  /// <summary>
  /// Точка фиксации: после успешной публикации всех элементов.
  /// После <see cref="Commit"/> вызов <see cref="Rollback"/> не выполняется.
  /// </summary>
  public void Commit()
  {
    // Точка фиксации: после неё откат не выполняется (one-shot), а оставшиеся .bak подлежат
    // best-effort очистке. Флаг хранится в самом журнале, чтобы случайный вызов Rollback
    // не удалил данные пользователя после успешной операции.
    _committed = true;
  }

  /// <summary>
  /// Best-effort удаление .bak/.tmp после <see cref="Commit"/>.
  /// Отказ очистки НЕ отменяет успех операции (семантика M4B).
  /// </summary>
  public void CleanupAfterCommit()
  {
    for (int i = 0; i < _files.Count; i++)
    {
      string? backupPath = _files[i].BackupPath;
      if (backupPath is null)
        continue;

      // Очистка после точки фиксации — best-effort: она не отменяет успех операции и НЕ СМЕЕТ
      // бросать исключение наружу (иначе публичный метод упадёт после успешного извлечения).
      try
      {
        if (!FileExists(backupPath))
          continue;

        // Backup наследует атрибуты прежнего файла (File.Move переносит их вместе с файлом),
        // включая ReadOnly, а удаление ReadOnly-файла на Windows отказывает. Без снятия атрибута
        // .bak оставался бы в каталоге назначения навсегда (измерено ревьюером на Windows).
        FileAttributes attrs = GetAttributes(backupPath);
        if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
          SetAttributes(backupPath, attrs & ~FileAttributes.ReadOnly);

        DeleteFile(backupPath);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
      catch (NotSupportedException) { }
      catch (ArgumentException) { }
      catch (System.Security.SecurityException) { }

      // Остаток .bak не отменяет успех; возможность остатка фиксируется в документации
      // как известное ограничение (гарантированная очистка не заявляется).
    }
  }

  /// <summary>
  /// <para>Откат ВСЕЙ операции: обратный порядок журнала, per-item catch.</para>
  /// <para>Порядок действий:</para>
  /// <list type="number">
  /// <item>Опубликованные <see cref="StagedEntryKind.CreatedNew"/> — удалить;</item>
  /// <item><see cref="StagedEntryKind.Replaced"/> — удалить опубликованный новый файл,
  /// затем восстановить backup (bak → final);</item>
  /// <item>Неопубликованные staged-файлы — удалить;</item>
  /// <item><see cref="StagedEntryKind.AttributesChanged"/> — восстановить исходное значение
  /// (до удаления каталогов);</item>
  /// <item><see cref="StagedEntryKind.CreatedDir"/> — удалить в обратном порядке (только пустые).</item>
  /// </list>
  /// <para>Файл, существовавший до операции и НЕ имеющий backup, не удаляется НИКОГДА.</para>
  /// <para>Откат выполняется один раз: повторный вызов не удаляет уже восстановленные прежние байты.</para>
  /// </summary>
  public void Rollback()
  {
    // После точки фиксации откат не выполняется: данные уже опубликованы успешно.
    if (_committed)
      return;

    // Идемпотентность по последствию: второй вызов не должен удалять восстановленный прежний файл.
    if (_rolledBack)
      return;

    _rolledBack = true;

    // a) Опубликованные файлы — в обратном порядке.
    for (int i = _files.Count - 1; i >= 0; i--)
    {
      var entry = _files[i];

      if (entry.Published)
      {
        if (entry.Kind == StagedEntryKind.CreatedNew)
        {
          // Опубликованный CreatedNew — удаляем.
          try { if (FileExists(entry.FinalPath)) DeleteFile(entry.FinalPath); }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }

          entry.Published = false;
        }
        else if (entry.Kind == StagedEntryKind.Replaced && entry.BackupPath is not null)
        {
          // Опубликованный Replaced — удаляем новый, восстанавливаем backup.
          try { if (FileExists(entry.FinalPath)) DeleteFile(entry.FinalPath); }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }

          try { if (FileExists(entry.BackupPath)) MoveFile(entry.BackupPath, entry.FinalPath); }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }

          // Состояние записи приводится к фактическому: файл больше не опубликован, а прежние
          // байты вернулись на своё имя. Без этого повторный откат удалил бы восстановленный файл.
          entry.Published = false;
        }
      }
      else
      {
        // Неопубликованный staged-файл — удаляем tmp.
        try { if (FileExists(entry.StagedPath)) DeleteFile(entry.StagedPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        // Если прежний файл уже уехал в .bak, а публикация не завершилась (отказ второго
        // переноса) — возвращаем его на место: инвариант «прежние байты на своём имени».
        if (entry.BackupPath is not null)
        {
          try
          {
            if (!FileExists(entry.FinalPath) && FileExists(entry.BackupPath))
              MoveFile(entry.BackupPath, entry.FinalPath);
          }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }
      }
    }

    // b) Отдельный проход по списку _unpublishedStaged: сюда попадают staged-файлы, созданные
    // CreateStagedPath и зарегистрированные TrackUnpublishedStaged, для которых Publish не вызывался
    // (например, writeAt вернул отрицательный результат). Проход по _files такие файлы не видит.
    for (int i = _unpublishedStaged.Count - 1; i >= 0; i--)
    {
      try { if (FileExists(_unpublishedStaged[i])) DeleteFile(_unpublishedStaged[i]); }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }

    // c) Атрибуты — восстанавливаем (fallback).
    for (int i = _attrChanges.Count - 1; i >= 0; i--)
    {
      var attr = _attrChanges[i];
      try
      {
        if (FileExists(attr.Path))
          SetAttributes(attr.Path, attr.OriginalAttributes);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }

    // d) Каталоги — удаляем в обратном порядке (только пустые).
    for (int i = _dirs.Count - 1; i >= 0; i--)
    {
      try
      {
        if (DirectoryExists(_dirs[i]))
          DeleteDirectory(_dirs[i]);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Отмечает staged-путь как «созданный, но ещё не опубликованный» — для удаления при откате,
  /// если запись завершилась сбоем до <see cref="Publish"/>.
  /// </summary>
  /// <param name="stagedPath">Путь staged-файла.</param>
  public void TrackUnpublishedStaged(string stagedPath)
  {
    _unpublishedStaged.Add(stagedPath);
  }

  /// <summary>
  /// Убирает staged-путь из списка неопубликованных (вызывается после успешной публикации,
  /// т.к. файл больше не по этому пути).
  /// </summary>
  /// <param name="stagedPath">Путь staged-файла.</param>
  public void RemoveUnpublishedStaged(string stagedPath)
  {
    _unpublishedStaged.Remove(stagedPath);
  }

  /// <summary>
  /// Регистрирует изменение атрибутов файла (для fallback-восстановления при откате).
  /// В ZIP-маршруте не используется: backup-first rename переносит атрибуты вместе с файлом.
  /// Подключается в шаге B6 (capability-probe ReadOnly/атрибуты), поэтому ветвь восстановления
  /// атрибутов в <see cref="Rollback"/> пока не имеет исполняемого вызывающего.
  /// </summary>
  /// <param name="path">Путь файла.</param>
  /// <param name="originalAttributes">Исходные атрибуты.</param>
  public void TrackAttributeChange(string path, FileAttributes originalAttributes)
  {
    _attrChanges.Add(new AttributeEntry { Path = path, OriginalAttributes = originalAttributes });
  }

  // Неопубликованные staged-файлы (созданы, но запись не удалась).
  private readonly List<string> _unpublishedStaged = new();

  /// <summary>
  /// На Windows приводит путь к расширенной форме <c>\\?\</c> (для UNC — <c>\\?\UNC\server\share</c>),
  /// если его длина достигает предела MAX_PATH, что позволяет работать с длинными путями БЕЗ
  /// long-path opt-in хоста (реестр LongPathsEnabled + longPathAware в манифесте приложения).
  /// Префикс отключает нормализацию Win32, поэтому путь обязан быть абсолютным и уже
  /// нормализованным (без <c>.</c> и <c>..</c>); наши пути строятся через Path.GetFullPath и TryBuildSafePath.
  /// На других ОС и для коротких/относительных/уже расширенных путей возвращает входной путь
  /// без изменения, поэтому поведение коротких путей не меняется вовсе.
  /// </summary>
  internal static string ToFileSystemPath(string path)
  {
    if (!OperatingSystem.IsWindows())
      return path;

    if (path.Length < WindowsMaxPath - 1)
      return path;

    if (path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
        path.StartsWith(@"\\.\", StringComparison.Ordinal))
      return path;

    if (!Path.IsPathRooted(path))
      return path;   // относительные пути всегда ограничены MAX_PATH, префикс неприменим

    string normalized = path.Replace('/', '\\');

    if (normalized.StartsWith(@"\\", StringComparison.Ordinal))
      return @"\\?\UNC\" + normalized.Substring(2);

    return @"\\?\" + normalized;
  }

  // Обёртки файловых вызовов: переводят путь через ToFileSystemPath для расширенной формы
  // на Windows. Логика журнала не меняется — только способ передачи пути в FS-API.
  private static bool FileExists(string path) => File.Exists(ToFileSystemPath(path));
  private static void MoveFile(string from, string to) => File.Move(ToFileSystemPath(from), ToFileSystemPath(to));
  private static void DeleteFile(string path) => File.Delete(ToFileSystemPath(path));
  private static FileAttributes GetAttributes(string path) => File.GetAttributes(ToFileSystemPath(path));
  private static void SetAttributes(string path, FileAttributes value) => File.SetAttributes(ToFileSystemPath(path), value);
  private static bool DirectoryExists(string path) => Directory.Exists(ToFileSystemPath(path));
  private static void DeleteDirectory(string path) => Directory.Delete(ToFileSystemPath(path), recursive: false);

  private static string GenerateHexSuffix()
  {
    Span<byte> bytes = stackalloc byte[8];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToHexString(bytes);
  }

  /// <summary>Число символов, которое имя стадии добавляет к полному пути: "." + 16 hex + ".tmp".</summary>
  internal const int StagedSuffixLength = 21;

  /// <summary>
  /// Предельная длина одного компонента имени файла. Одинакова для целевых платформ:
  /// NTFS ограничивает имя 255 символами UTF-16, POSIX — NAME_MAX (обычно 255 байт).
  /// </summary>
  internal const int MaxFileNameComponentLength = 255;

  /// <summary>
  /// Лимиты полного пути — документированные константы платформ (включают завершающий ноль):
  /// Windows <c>MAX_PATH</c> без long-path opt-in, macOS <c>MAXPATHLEN</c>, Linux <c>PATH_MAX</c>.
  /// </summary>
  internal const int WindowsMaxPath = 260;
  internal const int MacOsMaxPath = 1024;
  internal const int PosixMaxPath = 4096;

  /// <summary>
  /// Предел расширенного пути Windows при использовании префикса <c>\\?\</c> (документированное
  /// значение 32767 приблизительно и включает сам префикс). Применяется, потому что файловые
  /// вызовы маршрута переводятся на расширенную форму (<see cref="ToFileSystemPath"/>).
  /// </summary>
  internal const int WindowsExtendedMaxPath = 32767;

  /// <summary>
  /// Запас на префикс расширенного пути: <c>\\?\</c> добавляет 4 символа, а <c>\\?\UNC\</c>
  /// заменяет <c>\\</c> и добавляет 6 (итого +8). Вычитается из документированного предела,
  /// чтобы лимит описывал длину ИСХОДНОГО пути, а не пути с префиксом.
  /// </summary>
  internal const int ExtendedPathPrefixReserve = 8;

  /// <summary>
  /// Эффективный лимит длины полного пути для текущей ОС (без завершающего нуля и с учётом
  /// префикса расширенного пути). Используется и проверкой <see cref="IsPathTooLong"/>, и тестами,
  /// чтобы лимит имел одно определение.
  /// </summary>
  internal static int FullPathLimit
      => OperatingSystem.IsWindows() ? WindowsExtendedMaxPath - ExtendedPathPrefixReserve
       : OperatingSystem.IsMacOS() ? MacOsMaxPath
       : PosixMaxPath;

  /// <summary>
  /// Оценивает, выйдет ли имя стадии/backup для <paramref name="fullPath"/> за пределы платформы.
  /// Проверяются длина последнего компонента имени (с учётом суффикса стадии) и длина полного пути
  /// против <see cref="FullPathLimit"/>: на Windows это расширенный предел (файловые вызовы маршрута
  /// переводятся на префикс <c>\\?\</c>), на macOS <c>MAXPATHLEN</c> (1024), на прочих POSIX
  /// <c>PATH_MAX</c> (4096). Фактические значения на ubuntu/macos локально не измерены
  /// и подтверждаются capability-probe в CI; до подтверждения считаются неподтверждёнными.
  /// </summary>
  internal static bool IsPathTooLongForStaging(string fullPath)
      => IsPathTooLong(fullPath, StagedSuffixLength);

  /// <summary>
  /// Оценивает, выйдет ли <paramref name="fullPath"/> с учётом добавочного суффикса
  /// <paramref name="extraSuffixLength"/> за лимит, принятый в реализации (<see cref="FullPathLimit"/>).
  /// Единицы измерения соответствуют платформе: Windows считает лимит пути в символах UTF-16
  /// (документированный <c>MAX_PATH</c> и расширенный предел заданы в <c>WCHAR</c>),
  /// POSIX считает <c>NAME_MAX</c>/<c>PATH_MAX</c> в байтах UTF-8, поэтому для не-ASCII имён
  /// на POSIX измеряется именно длина в байтах (иначе проверка недооценивала бы длину
  /// и отказ возник бы на уровне ОС с маппингом в общий <c>IOError</c>).
  /// Фактические значения на ubuntu/macos локально не измерены и подтверждаются
  /// capability-probe в CI; до подтверждения считаются неподтверждёнными.
  /// </summary>
  internal static bool IsPathTooLong(string fullPath, int extraSuffixLength)
  {
    bool windows = OperatingSystem.IsWindows();
    string fileName = Path.GetFileName(fullPath);

    // Суффикс стадии удлиняет только последний компонент имени.
    int componentLength = windows ? fileName.Length : Encoding.UTF8.GetByteCount(fileName);
    if (componentLength + extraSuffixLength > MaxFileNameComponentLength)
      return true;

    // Лимит выбран по платформе: завышенное значение привело бы к тому, что отказ возникнет
    // на уровне ОС и будет смаплен в общий IOError, то есть шаг потерял бы смысл.
    // На Windows используется расширенный лимит (32767 минус запас на префикс), потому что
    // файловые вызовы маршрута переводятся на префикс \\?\ через ToFileSystemPath.
    int limit = FullPathLimit;
    int pathLength = windows ? fullPath.Length : Encoding.UTF8.GetByteCount(fullPath);

    return pathLength + extraSuffixLength >= limit;
  }

  private sealed class StagedFileEntry(
      StagedEntryKind kind, string finalPath, string stagedPath, string? backupPath)
  {
    public StagedEntryKind Kind { get; } = kind;
    public string FinalPath { get; } = finalPath;
    public string StagedPath { get; } = stagedPath;
    public string? BackupPath { get; } = backupPath;
    public bool Published { get; set; }
  }

  private struct AttributeEntry
  {
    public string Path;
    public FileAttributes OriginalAttributes;
  }
}
