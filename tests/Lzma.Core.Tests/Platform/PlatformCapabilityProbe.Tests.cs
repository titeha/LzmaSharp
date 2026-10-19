namespace Lzma.Core.Tests.Platform;

/// <summary>
/// Capability-probe платформенных допущений SEC-001 (todo #39). Каждый тест ИЗМЕРЯЕТ поведение
/// текущей ОС вместо того, чтобы считать его документированным, и падает с внятной причиной,
/// если допущение production-кода на этой платформе неверно. Тихих <c>return</c> нет
/// (правило 7 `03_TEST_QUALITY.md`): платформенная ветвь либо утверждает измеренное, либо
/// объясняет расхождение в сообщении.
/// <para>Что именно проверяется и зачем:</para>
/// <list type="bullet">
/// <item>константа <see cref="StagedExtractionJournal.MaxFileNameComponentLength"/> (255) —
/// не выше фактической возможности ФС, иначе pre-flight длины пути пропускал бы имя,
/// которое откажет на уровне ОС уже после публикации части элементов;</item>
/// <item><see cref="StagedExtractionJournal.FullPathLimit"/> — фактический предел полного пути
/// на этой ОС не ниже принятого лимита, и длинные пути работают через обёртку
/// <see cref="StagedExtractionJournal.ToFileSystemPath"/> БЕЗ opt-in хоста;</item>
/// <item>backup-first rename файла с атрибутом <c>ReadOnly</c> успешен и переносит атрибуты —
/// на этом держится сохранность прежних байтов и атрибутов при откате (B6, `TX-EX-06`);</item>
/// <item>удаление <c>ReadOnly</c>-файла: на Windows отказывает без снятия атрибута (поэтому
/// в <c>CleanupAfterCommit</c> атрибут снимается), на POSIX удаление определяется правами
/// каталога и проходит;</item>
/// <item>регистрозависимость тома — на ней основан выбор компаратора путей
/// (<c>OrdinalIgnoreCase</c> на Windows, <c>Ordinal</c> на POSIX) при поиске коллизий имён.</item>
/// </list>
/// <para>Честные границы: UNC-путь и хост с <c>LongPathsEnabled=0</c> в CI не воспроизводятся
/// (нет сетевого тома и нет права менять реестр хоста), поэтому они остаются непокрытыми;
/// независимость от opt-in подтверждается тем, что длинные пути создаются через расширенную
/// форму <c>\\?\</c> на любом хосте Windows.</para>
/// </summary>
public sealed class PlatformCapabilityProbeTests
{
  private static string NewRoot(string tag)
  {
    string root = Path.Combine(Path.GetTempPath(), $"lzsp-{tag}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(root));
    return root;
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(StagedExtractionJournal.ToFileSystemPath(root)))
        Directory.Delete(StagedExtractionJournal.ToFileSystemPath(root), recursive: true);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  /// <summary>
  /// Строит путь ровно <paramref name="targetLength"/> символов: цепочка каталогов компонентами
  /// по 255 символов и последний компонент — имя файла. Все обращения к ФС идут через
  /// <see cref="StagedExtractionJournal.ToFileSystemPath"/>, то есть ровно так, как это делает
  /// маршрут извлечения. Каталоги регистрируются в <paramref name="created"/> для очистки.
  /// </summary>
  private static string BuildPathOfLength(string root, int targetLength, List<string> created)
  {
    string current = root;

    while (true)
    {
      int remaining = targetLength - current.Length - 1; // -1 на разделитель
      if (remaining is >= 1 and <= StagedExtractionJournal.MaxFileNameComponentLength)
        return Path.Combine(current, new string('f', remaining));

      // Шаг подбирается так, чтобы остаток не «проскочил» ноль: после добавления каталога
      // длина имени файла остаётся ≥ 2.
      int step = Math.Min(StagedExtractionJournal.MaxFileNameComponentLength, remaining - 2);
      Assert.True(step >= 1,
          $"не удалось построить путь длины {targetLength} из корня \"{root}\" (остаток {remaining})");

      current = Path.Combine(current, new string('d', step));
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(current));
      created.Add(current);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // 1. NAME_MAX: константа 255 не выше возможности файловой системы
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Компонент имени в 255 символов создаётся, а 256 — отклоняется. Это и есть
  /// <see cref="StagedExtractionJournal.MaxFileNameComponentLength"/>: если бы ОС принимала
  /// меньше 255, pre-flight считал бы допустимым имя, которое откажет на уровне ОС.
  /// </summary>
  [Fact]
  public void Probe_NameMax_255Создаётся_256Отклоняется()
  {
    string root = NewRoot("name");
    try
    {
      string ok = Path.Combine(root, new string('f', StagedExtractionJournal.MaxFileNameComponentLength));
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(ok), [1, 2, 3]);
      Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(ok)),
          $"ОС не приняла компонент имени в {StagedExtractionJournal.MaxFileNameComponentLength} символов");

      string tooLong = Path.Combine(root, new string('f', StagedExtractionJournal.MaxFileNameComponentLength + 1));
      Exception? error = Record.Exception(() =>
          File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(tooLong), [1]));
      Assert.NotNull(error);
      Assert.False(File.Exists(StagedExtractionJournal.ToFileSystemPath(tooLong)),
          "компонент 256 создан — константа MaxFileNameComponentLength занижает возможность ОС " +
          "(безопасно для pre-flight, но требует правки лимита и документации)");
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // 2. PATH_MAX: фактический предел полного пути не ниже принятого лимита
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Путь длины <c>FullPathLimit - 1</c> создаётся через ту же обёртку, что и маршрут извлечения
  /// (на Windows — расширенная форма <c>\\?\</c>, то есть БЕЗ opt-in хоста <c>LongPathsEnabled</c>).
  /// Если создание откажет, принятый в реализации лимит выше фактического — pre-flight пропустил
  /// бы путь, который упадёт на уровне ОС после частичной публикации.
  /// </summary>
  [Fact]
  public void Probe_PathLimit_ПутьНаЕдиницуКорочеЛимитаСоздаётся()
  {
    int limit = StagedExtractionJournal.FullPathLimit;
    string root = NewRoot("path");
    var created = new List<string>();

    try
    {
      string path = BuildPathOfLength(root, limit - 1, created);
      Assert.Equal(limit - 1, path.Length);

      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [7]);

      Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(path)),
          $"ОС не приняла путь длины {limit - 1} при принятом в реализации лимите {limit} — " +
          "лимит завышен, pre-flight пропустит отказывающий путь");
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  /// <summary>
  /// Путь заведомо длиннее лимита отклоняется: либо ОС/рантайм бросают исключение, либо
  /// (если платформа принимает и более длинные пути) это фиксируется как расхождение принятой
  /// модели с фактом — лимит занижен, что безопасно для данных, но требует правки документации.
  /// </summary>
  [Fact]
  public void Probe_PathLimit_ПутьДлиннееЛимитаОтклоняется()
  {
    int limit = StagedExtractionJournal.FullPathLimit;
    string root = NewRoot("over");
    var created = new List<string>();

    try
    {
      Exception? buildError = null;
      string? path = null;
      try
      {
        path = BuildPathOfLength(root, limit + 64, created);
      }
      catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
      {
        buildError = ex; // каталог за пределами лимита не создался — это и есть отказ платформы
      }

      if (buildError is not null)
        return; // измерено: платформа отказала уже на создании цепочки каталогов

      Assert.NotNull(path);
      Exception? writeError = Record.Exception(() =>
          File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [7]));

      if (writeError is null)
      {
        // Платформа приняла путь длиннее принятого лимита. Данные при этом не теряются
        // (отказ был бы консервативным), но факт обязан быть видимым, а не тихим.
        Assert.Fail(
            $"ОС приняла путь длины {limit + 64} при FullPathLimit={limit}: лимит в реализации " +
            "ниже фактического, требуется правка константы и документации");
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // 3. Rename ReadOnly-файла: основа сохранности прежних байтов и атрибутов
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Backup-first rename файла с атрибутом <c>ReadOnly</c> успешен на всех целевых ОС и переносит
  /// атрибуты вместе с файлом, а обратный rename возвращает и байты, и атрибуты. На этом держится
  /// гарантия отката (B6, `TX-EX-06`): прежний файл не перезаписывается, а уезжает в <c>.bak</c>
  /// тем же файловым объектом.
  /// </summary>
  [Fact]
  public void Probe_Rename_ReadOnlyФайлПереноситсяСАтрибутами_ИВозвращаетсяОбратно()
  {
    string root = NewRoot("ro");
    string path = Path.Combine(root, "sentinel.bin");
    string backup = Path.Combine(root, "sentinel.bin.bak");
    byte[] original = [1, 2, 3, 4, 5];

    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), original);
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), FileAttributes.ReadOnly);

      FileAttributes before = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
      Assert.NotEqual(0, (int)(before & FileAttributes.ReadOnly));

      // Прямой ход публикации: прежний файл → backup.
      File.Move(StagedExtractionJournal.ToFileSystemPath(path), StagedExtractionJournal.ToFileSystemPath(backup));

      FileAttributes moved = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(backup));
      Assert.Equal(original, File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(backup)));
      Assert.Equal(before & FileAttributes.ReadOnly, moved & FileAttributes.ReadOnly);

      // Обратный ход отката: backup → прежнее имя.
      File.Move(StagedExtractionJournal.ToFileSystemPath(backup), StagedExtractionJournal.ToFileSystemPath(path));

      FileAttributes restored = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
      Assert.Equal(original, File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(path)));
      Assert.Equal(before & FileAttributes.ReadOnly, restored & FileAttributes.ReadOnly);

      // Снятие атрибута возвращает файлу записываемость (нужно для очистки .bak на Windows).
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), restored & ~FileAttributes.ReadOnly);
      Assert.Equal(0, (int)(File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path)) & FileAttributes.ReadOnly));
    }
    finally
    {
      TryClearReadOnly(path);
      TryClearReadOnly(backup);
      TryDeleteTree(root);
    }
  }

  private static void TryClearReadOnly(string path)
  {
    try
    {
      if (File.Exists(StagedExtractionJournal.ToFileSystemPath(path)))
      {
        FileAttributes attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), attrs & ~FileAttributes.ReadOnly);
      }
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  /// <summary>
  /// Удаление <c>ReadOnly</c>-файла: на Windows отказывает, пока атрибут не снят (именно поэтому
  /// <c>CleanupAfterCommit</c> снимает атрибут перед удалением <c>.bak</c>), на POSIX удаление
  /// определяется правами каталога и проходит. После снятия атрибута удаление успешно везде —
  /// это и делает production-ветвь.
  /// </summary>
  [Fact]
  public void Probe_Delete_ReadOnlyФайла_ОтличаетсяПоПлатформам_ИУспешноПослеСнятияАтрибута()
  {
    string root = NewRoot("del");
    string path = Path.Combine(root, "locked.bin");

    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [9]);
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), FileAttributes.ReadOnly);

      if (OperatingSystem.IsWindows())
      {
        Assert.Throws<UnauthorizedAccessException>(
            () => File.Delete(StagedExtractionJournal.ToFileSystemPath(path)));
        Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(path)),
            "ReadOnly-файл не должен удаляться без снятия атрибута — иначе ветвь CleanupAfterCommit " +
            "на Windows не нужна, а её наличие в коде ложно");
      }
      else
      {
        // На POSIX unlink определяется правом записи на каталог, а не атрибутом файла:
        // удаление проходит. Это измеренное расхождение с Windows, а не пропуск проверки.
        File.Delete(StagedExtractionJournal.ToFileSystemPath(path));
        Assert.False(File.Exists(StagedExtractionJournal.ToFileSystemPath(path)));

        File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [9]);
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), FileAttributes.ReadOnly);
      }

      // Общий для всех ОС вывод production-ветви: после снятия атрибута файл удаляется.
      FileAttributes attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), attrs & ~FileAttributes.ReadOnly);
      File.Delete(StagedExtractionJournal.ToFileSystemPath(path));
      Assert.False(File.Exists(StagedExtractionJournal.ToFileSystemPath(path)));
    }
    finally
    {
      TryClearReadOnly(path);
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // 4. Регистрозависимость тома: основание выбора компаратора путей
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Измеряет регистрозависимость тома: на Windows имена <c>a.txt</c> и <c>A.TXT</c> — один объект
  /// (поэтому коллизии ищутся <c>OrdinalIgnoreCase</c>), на POSIX — два разных (поэтому <c>Ordinal</c>).
  /// Расхождение с ожидаемым поведением платформы означало бы, что выбор компаратора в маршруте
  /// извлечения не соответствует тому тому, на котором выполняются тесты.
  /// </summary>
  [Fact]
  public void Probe_РегистрозависимостьТома_СоответствуетВыбранномуКомпаратору()
  {
    string root = NewRoot("case");
    string lower = Path.Combine(root, "a.txt");
    string upper = Path.Combine(root, "A.TXT");

    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(lower), [1]);
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(upper), [2]);

      int files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(root)).Length;

      if (OperatingSystem.IsWindows())
      {
        Assert.Equal(1, files);
        Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(upper)),
            "Windows: имя в другом регистре обязано находить тот же файл — иначе OrdinalIgnoreCase " +
            "в поиске коллизий неверен");
      }
      else
      {
        Assert.Equal(2, files);
        Assert.Equal([1], File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(lower)));
        Assert.Equal([2], File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(upper)));
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }
}
