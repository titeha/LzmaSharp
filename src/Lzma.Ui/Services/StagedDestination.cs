namespace Lzma.Ui.Services;

/// <summary>
/// Staged-назначение для создания архива (SEC-002, SECURITY_REMEDIATION_PLAN.md §4.3):
/// временный файл на той же файловой системе, что и конечный путь. Выходной поток
/// передаётся writer-у в staged-файл, а конечный путь затрагивается только методом
/// <see cref="Commit"/> после успешного завершения операции.
/// </summary>
/// <remarks>
/// Шаги 1–3 §4.4: staged-путь вычисляется рядом с назначением;
/// <see cref="OpenWrite"/> пишет в staged-файл, <see cref="Commit"/> переносит
/// его в назначение, <see cref="Dispose"/> убирает staged-файл без публикации.
/// </remarks>
internal sealed class StagedDestination : System.IDisposable
{
  /// <summary>Путь назначения создаваемого архива.</summary>
  private readonly string _destinationPath;

  /// <summary>Путь staged-файла на той же файловой системе, что и назначение.</summary>
  private readonly string _stagedPath;

  /// <summary>Была ли успешная публикация staged-файла в назначение.</summary>
  private bool _committed;

  /// <summary>
  /// Инициализирует новый экземпляр класса <see cref="StagedDestination"/>
  /// и вычисляет путь staged-файла рядом с назначением.
  /// </summary>
  /// <param name="destinationPath">Путь назначения создаваемого архива.</param>
  /// <exception cref="ArgumentException">Путь назначения пуст или состоит из пробелов.</exception>
  public StagedDestination(string destinationPath)
  {
    if (string.IsNullOrWhiteSpace(destinationPath))
    {
      throw new ArgumentException("Путь назначения не может быть пустым.", nameof(destinationPath));
    }

    _destinationPath = destinationPath;
    _stagedPath = BuildStagedPath(destinationPath);
  }

  /// <summary>Путь назначения создаваемого архива.</summary>
  public string DestinationPath => _destinationPath;

  /// <summary>Путь staged-файла на той же файловой системе, что и назначение.</summary>
  public string StagedPath => _stagedPath;

  /// <summary>
  /// Открывает seekable-поток для записи в staged-файл.
  /// Staged-файл создаётся заново, доступ монопольный.
  /// </summary>
  /// <returns>Seekable-поток для записи архива.</returns>
  public Stream OpenWrite()
  {
    return new FileStream(_stagedPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
  }

  /// <summary>
  /// Коммитит staged-файл в <see cref="DestinationPath"/> переносом
  /// с заменой существующего файла. Вызывать только после успешного
  /// завершения операции. После коммита <see cref="Dispose"/> файл не удаляет.
  /// </summary>
  public void Commit()
  {
    File.Move(_stagedPath, _destinationPath, overwrite: true);
    _committed = true;
  }

  /// <summary>
  /// Rollback/cleanup: если <see cref="Commit"/> не было, удаляет staged-файл.
  /// Ошибки удаления глотаются — очистка не должна маскировать исходную ошибку операции.
  /// </summary>
  public void Dispose()
  {
    if (_committed)
    {
      return;
    }

    try
    {
      File.Delete(_stagedPath);
    }
    catch (IOException)
    {
      // Очистка best-effort: файл мог быть уже удалён или оказаться недоступным.
    }
    catch (UnauthorizedAccessException)
    {
      // Очистка best-effort: нет доступа к staged-файлу.
    }
  }

  /// <summary>
  /// Строит путь staged-файла: тот же каталог, что и у назначения
  /// (та же файловая система — коммит переносом без копирования),
  /// уникальное имя из имени назначения и GUID.
  /// </summary>
  private static string BuildStagedPath(string destinationPath)
  {
    string? directory = Path.GetDirectoryName(destinationPath);
    if (string.IsNullOrEmpty(directory))
    {
      // Относительный путь без каталога — staged-файл рядом, в текущем каталоге.
      directory = ".";
    }

    string fileName = Path.GetFileName(destinationPath);
    return Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}.tmp");
  }
}
