using Lzma.Core.SevenZip;

namespace Lzma.Ui.Services;

/// <summary>
/// Staged-набор томов для многотомного создания архива (SEC-002,
/// SECURITY_REMEDIATION_PLAN.md §4.4 шаг 10): тома пишутся рядом с назначением
/// под временной staged-базой (<c>{StagedBasePath}.001/.002/…</c>), а конечные
/// имена <c>{DestinationBasePath}.NNN</c> затрагиваются только методом
/// <see cref="Commit"/> после успешного завершения операции.
/// </summary>
/// <remarks>
/// Шаги 10.1–10.3: staged-база вычисляется рядом с назначением, manifest
/// заполняется через <see cref="SetVolumes"/>, <see cref="Commit"/> переносит
/// тома в конечные имена, <see cref="Dispose"/> убирает staged-тома без
/// публикации. Подключение к сервису — шаг 10.4.
/// </remarks>
internal sealed class StagedVolumeSet : System.IDisposable
{
  /// <summary>Seam файловых операций (инъекция для тестов/детерминированных отказов).</summary>
  private readonly IStagedVolumeFileOperations _fileOperations;

  /// <summary>Базовый путь назначения; тома получают суффиксы .001/.002/…</summary>
  private readonly string _destinationBasePath;

  /// <summary>Staged-база рядом с назначением.</summary>
  private readonly string _stagedBasePath;

  /// <summary>Manifest: staged-тома, зафиксированные для commit/cleanup.</summary>
  private readonly List<string> _manifest = [];

  /// <summary>Была ли успешная публикация staged-томов в назначение.</summary>
  private bool _committed;

  /// <summary>
  /// Инициализирует новый экземпляр класса <see cref="StagedVolumeSet"/>
  /// и вычисляет staged-базу рядом с назначением.
  /// </summary>
  /// <param name="destinationBasePath">Базовый путь назначения многотомного архива.</param>
  /// <exception cref="ArgumentException">Базовый путь пуст или состоит из пробелов.</exception>
  public StagedVolumeSet(string destinationBasePath)
      : this(destinationBasePath, new StagedVolumeFileOperations())
  {
  }

  /// <summary>
  /// Конструктор для тестовой инъекции seam файловых операций.
  /// </summary>
  /// <param name="destinationBasePath">Базовый путь назначения многотомного архива.</param>
  /// <param name="fileOperations">Seam файловых операций (не может быть null).</param>
  /// <exception cref="ArgumentNullException">fileOperations равен null.</exception>
  /// <exception cref="ArgumentException">Базовый путь пуст или состоит из пробелов.</exception>
  internal StagedVolumeSet(string destinationBasePath, IStagedVolumeFileOperations fileOperations)
  {
    ArgumentNullException.ThrowIfNull(fileOperations);

    if (string.IsNullOrWhiteSpace(destinationBasePath))
    {
      throw new ArgumentException("Базовый путь томов не может быть пустым.", nameof(destinationBasePath));
    }

    _fileOperations = fileOperations;
    _destinationBasePath = destinationBasePath;
    _stagedBasePath = BuildStagedBasePath(destinationBasePath);
  }

  /// <summary>Базовый путь назначения; тома получают суффиксы .001/.002/…</summary>
  public string DestinationBasePath => _destinationBasePath;

  /// <summary>Staged-база рядом с назначением: тома пишутся как <c>{StagedBasePath}.001/.002/…</c>.</summary>
  public string StagedBasePath => _stagedBasePath;

  /// <summary>Manifest: staged-тома, зафиксированные для commit/cleanup.</summary>
  public IReadOnlyList<string> Manifest => _manifest;

  /// <summary>
  /// Фиксирует в manifest созданные staged-тома. Вызывается после полного
  /// завершения записи, до <see cref="Commit"/>.
  /// </summary>
  /// <param name="stagedVolumePaths">Пути созданных staged-томов в порядке .001, .002, …</param>
  /// <exception cref="InvalidOperationException">Manifest уже заполнен.</exception>
  public void SetVolumes(IReadOnlyList<string> stagedVolumePaths)
  {
    ArgumentNullException.ThrowIfNull(stagedVolumePaths);

    if (_manifest.Count > 0)
    {
      throw new InvalidOperationException("Manifest staged-томов уже заполнен.");
    }

    _manifest.AddRange(stagedVolumePaths);
  }

  /// <summary>
  /// Коммитит staged-тома в конечные имена <c>{DestinationBasePath}.NNN</c> с заменой
  /// существующих и удаляет лишние старые тома, если новый набор короче.
  /// Вызывать только после успешного завершения операции и <see cref="SetVolumes"/>.
  /// Ошибки переноса/удаления пробрасываются: частичная публикация или лишний старый
  /// том делают многотомный набор нечитаемым.
  /// </summary>
  /// <exception cref="InvalidOperationException">Manifest не заполнен.</exception>
  public void Commit()
  {
    if (_manifest.Count == 0)
    {
      throw new InvalidOperationException("Manifest staged-томов не заполнен.");
    }

    // Резервная фаза: до публикации ни одного нового тома переносим все существующие
    // управляемые конечные тома в уникальные backup-пути (в том же каталоге).
    var backups = new List<(string FinalPath, string BackupPath)>();
    try
    {
      BackupFinalVolumes(backups);
    }
    catch (Exception backupFailure)
    {
      // Сбой внутри backup-фазы: новые тома ещё не публиковались, откатываем уже
      // созданные резервные копии в обратном порядке и сохраняем исходное исключение
      // как первичное. Откат не должен молча глотать собственные ошибки.
      Exception? restoreFailure = null;
      try
      {
        RestoreBackups(backups);
      }
      catch (Exception ex)
      {
        restoreFailure = ex;
      }

      if (restoreFailure is not null)
      {
        throw new AggregateException(
            "Ошибка backup-фазы с последующим сбоем отката резервных копий.",
            backupFailure,
            restoreFailure);
      }

      throw;
    }

    // Публикация: перенос staged-томов в конечные имена. После успешной резервной
    // фазы управляемые конечные имена уже освобождены, поэтому используем
    // overwrite:false — не затираем неожиданно появившийся объект.
    var published = new List<string>();
    try
    {
      for (int i = 0; i < _manifest.Count; i++)
      {
        string finalPath = VolumeSpanningWriteStream.VolumePath(_destinationBasePath, i);
        _fileOperations.Move(_manifest[i], finalPath, overwrite: false);
        published.Add(finalPath);
      }
    }
    catch (Exception publishFailure) when (IsControlledFailure(publishFailure))
    {
      // Сбой публикации: удаляем уже опубликованные новые конечные тома (в обратном
      // порядке), восстанавливаем все успешные backup и чистим staged-файлы best-effort.
      // Первичное исключение публикации сохраняется; ошибки отката не глотаются.
      var rollbackErrors = new List<Exception>();
      RollbackPublish(published, backups, rollbackErrors);

      _committed = false;

      if (rollbackErrors.Count == 0)
      {
        throw;
      }

      throw new AggregateException(
          "Ошибка публикации томов с последующим сбоем отката.",
          [publishFailure, .. rollbackErrors]);
    }

    // Устаревшие тома: старый набор мог быть длиннее нового. Имена томов идут
    // без пропусков (.001/.002/…), поэтому чистим подряд до первого отсутствующего.
    for (int i = _manifest.Count; ; i++)
    {
      string stale = VolumeSpanningWriteStream.VolumePath(_destinationBasePath, i);
      if (!_fileOperations.Exists(stale))
      {
        break;
      }

      _fileOperations.Delete(stale);
    }

    _committed = true;
  }

  /// <summary>
  /// Откатывает сбой публикации в фиксированном порядке: удаляет опубликованные новые
  /// конечные тома в обратном порядке публикации, затем восстанавливает backup-копии в
  /// обратном порядке резервирования, затем best-effort чистит staged-файлы manifest.
  /// Ошибки каждой операции собираются в <paramref name="errors"/> без проброса.
  /// </summary>
  private void RollbackPublish(
      List<string> published,
      List<(string FinalPath, string BackupPath)> backups,
      List<Exception> errors)
  {
    // 1. Удаляем только успешно опубликованные новые конечные тома, в обратном порядке.
    for (int i = published.Count - 1; i >= 0; i--)
    {
      TryDelete(published[i], errors);
    }

    // 2. Восстанавливаем все успешно созданные backup-копии в обратном порядке.
    for (int i = backups.Count - 1; i >= 0; i--)
    {
      (string finalPath, string backupPath) = backups[i];
      try
      {
        _fileOperations.Move(backupPath, finalPath, overwrite: false);
      }
      catch (Exception ex) when (IsControlledFailure(ex))
      {
        errors.Add(ex);
      }
    }

    // 3. Best-effort чистим оставшиеся staged-файлы текущего manifest.
    foreach (string staged in _manifest)
    {
      TryDelete(staged, errors);
    }
  }

  /// <summary>
  /// Удаляет файл, собирая контролируемые ошибки файловой системы в <paramref name="errors"/>.
  /// </summary>
  private void TryDelete(string path, List<Exception> errors)
  {
    try
    {
      _fileOperations.Delete(path);
    }
    catch (Exception ex) when (IsControlledFailure(ex))
    {
      errors.Add(ex);
    }
  }

  /// <summary>
  /// Контролируемые отказы файловой системы, для которых выполняется rollback.
  /// Фатальные ошибки процесса (OOM/StackOverflow/AccessViolation и т.п.) не перехватываются.
  /// </summary>
  private static bool IsControlledFailure(Exception ex)
      => ex is IOException or UnauthorizedAccessException;

  /// <summary>
  /// Переносит все существующие управляемые конечные тома (по числу записей staged
  /// manifest) в уникальные backup-пути в том же каталоге. Успешно созданная резервная
  /// копия регистрируется в <paramref name="backups"/> сразу после каждого Move.
  /// </summary>
  private void BackupFinalVolumes(List<(string FinalPath, string BackupPath)> backups)
  {
    for (int i = 0; i < _manifest.Count; i++)
    {
      string finalPath = VolumeSpanningWriteStream.VolumePath(_destinationBasePath, i);
      if (!_fileOperations.Exists(finalPath))
      {
        continue;
      }

      string backupPath = BuildBackupPath(finalPath);
      _fileOperations.Move(finalPath, backupPath, overwrite: false);
      backups.Add((finalPath, backupPath));
    }
  }

  /// <summary>
  /// Откатывает созданные резервные копии в обратном порядке: backup → исходный конечный
  /// путь. Конечные пути, для которых backup не был создан, и посторонние файлы не
  /// затрагиваются.
  /// </summary>
  private void RestoreBackups(List<(string FinalPath, string BackupPath)> backups)
  {
    for (int i = backups.Count - 1; i >= 0; i--)
    {
      (string finalPath, string backupPath) = backups[i];
      _fileOperations.Move(backupPath, finalPath, overwrite: false);
    }
  }

  /// <summary>
  /// Строит уникальный backup-путь в том же каталоге, что и конечный том.
  /// </summary>
  private static string BuildBackupPath(string finalPath)
  {
    string? directory = Path.GetDirectoryName(finalPath);
    if (string.IsNullOrEmpty(directory))
    {
      directory = ".";
    }

    string fileName = Path.GetFileName(finalPath);
    return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.bak");
  }

  /// <summary>
  /// Rollback/cleanup: если <see cref="Commit"/> не было, удаляет staged-тома.
  /// Кроме manifest сканирует диск: запись могла прерваться до <see cref="SetVolumes"/>.
  /// Ошибки удаления глотаются — очистка не должна маскировать исходную ошибку операции.
  /// </summary>
  public void Dispose()
  {
    if (_committed)
    {
      return;
    }

    var victims = new List<string>(_manifest);
    foreach (string path in ProbeStagedVolumes())
    {
      if (!victims.Contains(path))
      {
        victims.Add(path);
      }
    }

    foreach (string path in victims)
    {
      try
      {
        _fileOperations.Delete(path);
      }
      catch (IOException)
      {
        // Очистка best-effort: файл мог быть уже удалён или оказаться недоступным.
      }
      catch (UnauthorizedAccessException)
      {
        // Очистка best-effort: нет доступа к staged-тому.
      }
    }
  }

  /// <summary>
  /// Сканирует staged-тома на диске подряд от <c>{StagedBasePath}.001</c> до первого пропуска.
  /// </summary>
  private List<string> ProbeStagedVolumes()
  {
    var found = new List<string>();
    for (int i = 0; ; i++)
    {
      string path = VolumeSpanningWriteStream.VolumePath(_stagedBasePath, i);
      if (!_fileOperations.Exists(path))
      {
        break;
      }

      found.Add(path);
    }

    return found;
  }

  /// <summary>
  /// Строит путь staged-базы: тот же каталог, что и у базы назначения
  /// (та же файловая система — коммит переносом без копирования),
  /// уникальное имя из имени базы, GUID и маркера многотомности.
  /// </summary>
  private static string BuildStagedBasePath(string destinationBasePath)
  {
    string? directory = Path.GetDirectoryName(destinationBasePath);
    if (string.IsNullOrEmpty(directory))
    {
      // Относительный путь без каталога — staged-база рядом, в текущем каталоге.
      directory = ".";
    }

    string fileName = Path.GetFileName(destinationBasePath);
    return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.volumes.tmp");
  }
}
