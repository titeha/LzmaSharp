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
  {
    if (string.IsNullOrWhiteSpace(destinationBasePath))
    {
      throw new ArgumentException("Базовый путь томов не может быть пустым.", nameof(destinationBasePath));
    }

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

    // Перенос staged-томов в конечные имена с заменой существующих.
    for (int i = 0; i < _manifest.Count; i++)
    {
      File.Move(_manifest[i], VolumeSpanningWriteStream.VolumePath(_destinationBasePath, i), overwrite: true);
    }

    // Устаревшие тома: старый набор мог быть длиннее нового. Имена томов идут
    // без пропусков (.001/.002/…), поэтому чистим подряд до первого отсутствующего.
    for (int i = _manifest.Count; ; i++)
    {
      string stale = VolumeSpanningWriteStream.VolumePath(_destinationBasePath, i);
      if (!File.Exists(stale))
      {
        break;
      }

      File.Delete(stale);
    }

    _committed = true;
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
        File.Delete(path);
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
      if (!File.Exists(path))
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
