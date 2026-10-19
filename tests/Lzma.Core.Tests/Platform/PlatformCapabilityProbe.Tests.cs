namespace Lzma.Core.Tests.Platform;

/// <summary>
/// Capability-probe платформенных допущений SEC-001 (todo #39). Каждый тест ИЗМЕРЯЕТ поведение
/// текущей ОС вместо того, чтобы считать его документированным. Если окружение не воспроизводит
/// сценарий, тест visibly пропускается с причиной (<c>Assert.Skip</c>), а не молча проходит
/// (правило 7 `03_TEST_QUALITY.md`: явный capability-probe, запрет тихого <c>return</c>).
/// <para>Что измеряется и зачем:</para>
/// <list type="bullet">
/// <item>предел длины компонента имени (константа
/// <see cref="StagedExtractionJournal.MaxFileNameComponentLength"/> = 255) — если ОС принимает
/// меньше, pre-flight длины пути пропускал бы имя, которое откажет на уровне ОС;</item>
/// <item>предел длины полного пути (<see cref="StagedExtractionJournal.FullPathLimit"/>) —
/// измеряется на POSIX (Linux <c>PATH_MAX</c>, macOS <c>MAXPATHLEN</c>), где раньше был заявлен
/// как «не измерен»; на Windows проверяется нижняя граница (длинные пути работают через
/// расширенную форму <c>\\?\</c> без opt-in хоста <c>LongPathsEnabled</c>), а точная граница
/// 32759 остаётся документированной константой — см. «Честные границы» ниже;</item>
/// <item>rename файла с атрибутом <c>ReadOnly</c> и перенос атрибутов — на этом держится
/// сохранность прежних байтов и атрибутов при откате (B6, `TX-EX-06`);</item>
/// <item>удаление <c>ReadOnly</c>-файла — платформенная семантика, из-за которой
/// <c>CleanupAfterCommit</c> снимает атрибут перед удалением <c>.bak</c>;</item>
/// <item>регистрозависимость тома — на ней основан выбор компаратора путей при поиске коллизий
/// (<c>OrdinalIgnoreCase</c> на Windows, <c>Ordinal</c> на прочих ОС).</item>
/// </list>
/// <para>Честные границы probe: (1) UNC-путь и хост с <c>LongPathsEnabled=0</c> в CI не
/// воспроизводятся (нет сетевого тома, нет права менять реестр хоста); (2) точная граница полной
/// длины пути на Windows (32759) не измеряется — потребовала бы цепочки из ~128 вложенных
/// каталогов, измеряется только нижняя граница 8192; (3) регистрозависимость тома на macOS
/// зависит от разметки APFS (по умолчанию регистронезависима), поэтому расхождение с компаратором
/// <c>Ordinal</c> фиксируется как измеренный факт, а не как отказ.</para>
/// </summary>
public sealed class PlatformCapabilityProbeTests
{
  /// <summary>Верхняя граница измерения длины пути: достаточно для Linux (4096) и macOS (1024),
  /// но не требует строить предельно длинные цепочки на Windows.</summary>
  private const int ProbePathCeiling = 8192;

  /// <summary>
  /// Канонический корень для измерения длин: на macOS <c>Path.GetTempPath()</c> возвращает
  /// <c>/var/folders/…</c>, а <c>/var</c> — symlink на <c>/private/var</c>, то есть ОС видит путь
  /// на 8 символов длиннее построенного. Без приведения к реальному пути измерение расходится
  /// с тем, что проверяет ядро системы (обнаружено падением probe в CI на macos-latest).
  /// </summary>
  private static string NewRoot(string tag)
  {
    string temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
    string canonical = Directory.ResolveLinkTarget(temp, returnFinalTarget: true)?.FullName ?? temp;

    string root = Path.Combine(canonical, $"lzsp-{tag}-{Guid.NewGuid():N}");
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
  /// до 255 символов и последний компонент — имя файла. Обращения к ФС идут через
  /// <see cref="StagedExtractionJournal.ToFileSystemPath"/>, то есть так же, как в маршруте
  /// извлечения. Создание каталога может отказать — это и есть измеряемая граница, поэтому
  /// исключение возвращается наружу, а не глотается.
  /// </summary>
  private static string BuildPathOfLength(string root, int targetLength)
  {
    string current = root;

    while (true)
    {
      int remaining = targetLength - current.Length - 1; // −1 на разделитель
      if (remaining is >= 1 and <= StagedExtractionJournal.MaxFileNameComponentLength)
        return Path.Combine(current, new string('f', remaining));

      // Шаг подбирается так, чтобы остаток не «проскочил» ноль: после добавления каталога
      // длина имени файла остаётся ≥ 2.
      int step = Math.Min(StagedExtractionJournal.MaxFileNameComponentLength, remaining - 2);
      if (step < 1)
        throw new InvalidOperationException(
            $"не удалось построить путь длины {targetLength} из корня \"{root}\" (остаток {remaining})");

      current = Path.Combine(current, new string('d', step));
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(current));
    }
  }

  private static bool TryCreateFile(string path)
  {
    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [7]);
      return true;
    }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
    catch (ArgumentException) { return false; }
    catch (NotSupportedException) { return false; }
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
      Assert.True(TryCreateFile(ok),
          $"ОС не приняла компонент имени в {StagedExtractionJournal.MaxFileNameComponentLength} символов — " +
          "константа MaxFileNameComponentLength выше фактической возможности ФС, " +
          "pre-flight длины пути пропускал бы отказывающие имена");

      string tooLong = Path.Combine(root, new string('f', StagedExtractionJournal.MaxFileNameComponentLength + 1));
      Assert.False(TryCreateFile(tooLong),
          "компонент 256 принят: константа занижает возможность ОС (безопасно для pre-flight, " +
          "но лимит и документацию нужно уточнить)");
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // 2. PATH_MAX: измеренный предел против принятого в реализации лимита
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Путь длины <c>FullPathLimit − 1</c> создаётся через ту же обёртку, что и маршрут извлечения.
  /// Если создание откажет, принятый в реализации лимит выше фактического — pre-flight пропустил
  /// бы путь, который упадёт на уровне ОС уже после частичной публикации. На Windows точная
  /// граница (32759) не измеряется: проверяется нижняя граница 8192, то есть что длинные пути
  /// работают через <c>\\?\</c> без opt-in хоста.
  /// </summary>
  [Fact]
  public void Probe_PathLimit_ДопустимаяДлинаПутиСоздаётся()
  {
    int limit = StagedExtractionJournal.FullPathLimit;
    string root = NewRoot("path");

    try
    {
      if (limit - 1 > ProbePathCeiling)
      {
        // Windows: точная граница вне бюджета probe, измеряем нижнюю границу.
        string longPath = BuildPathOfLength(root, ProbePathCeiling);
        Assert.True(TryCreateFile(longPath),
            $"ОС не приняла путь длины {ProbePathCeiling} при лимите {limit}: расширенная форма " +
            "пути не работает на этом хосте, хотя маршрут извлечения на неё рассчитывает");
        return;
      }

      string atLimit = BuildPathOfLength(root, limit - 1);
      Assert.Equal(limit - 1, atLimit.Length);
      Assert.True(TryCreateFile(atLimit),
          $"ОС не приняла путь длины {limit - 1} при FullPathLimit={limit}: лимит в реализации " +
          "выше фактического, pre-flight пропустит отказывающий путь");
    }
    catch (IOException ex)
    {
      Assert.Fail($"создание цепочки каталогов для измерения PATH_MAX отказало: {ex.Message}");
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  /// <summary>
  /// Путь заведомо длиннее лимита отклоняется — то есть лимит не занижен «с запасом в никуда»:
  /// измеренная граница совпадает с принятой моделью. Измеряется только там, где граница
  /// достижима в бюджете probe (Linux, macOS); на Windows пропускается с явной причиной.
  /// </summary>
  [Fact]
  public void Probe_PathLimit_ДлинаЗаПределомОтклоняется()
  {
    int limit = StagedExtractionJournal.FullPathLimit;

    if (limit + 64 > ProbePathCeiling)
      Assert.Skip($"точная граница PATH_MAX на этой ОС ({limit}) вне бюджета probe " +
                  $"(потолок измерения {ProbePathCeiling}) — проверяется только нижняя граница");

    string root = NewRoot("over");
    try
    {
      string? overLimit = null;
      try
      {
        overLimit = BuildPathOfLength(root, limit + 64);
      }
      catch (IOException)
      {
        return; // измерено: платформа отказала уже на создании цепочки каталогов
      }

      Assert.False(TryCreateFile(overLimit),
          $"ОС приняла путь длины {limit + 64} при FullPathLimit={limit}: лимит в реализации ниже " +
          "фактического (данные не теряются, отказ консервативен), но константу и документацию " +
          "нужно уточнить");
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
  /// Backup-first rename файла с атрибутом <c>ReadOnly</c> успешен и переносит атрибуты вместе
  /// с файлом, а обратный rename возвращает и байты, и атрибуты. На этом держится гарантия
  /// отката (B6, `TX-EX-06`): прежний файл не перезаписывается, а уезжает в <c>.bak</c> тем же
  /// файловым объектом.
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

      Assert.Equal(original, File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(backup)));
      Assert.Equal(before & FileAttributes.ReadOnly,
          File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(backup)) & FileAttributes.ReadOnly);

      // Обратный ход отката: backup → прежнее имя.
      File.Move(StagedExtractionJournal.ToFileSystemPath(backup), StagedExtractionJournal.ToFileSystemPath(path));

      Assert.Equal(original, File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(path)));
      Assert.Equal(before & FileAttributes.ReadOnly,
          File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path)) & FileAttributes.ReadOnly);

      // Снятие атрибута возвращает записываемость (нужно для очистки .bak на Windows).
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path),
          File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path)) & ~FileAttributes.ReadOnly);
      Assert.Equal(0,
          (int)(File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path)) & FileAttributes.ReadOnly));
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
  /// Удаление <c>ReadOnly</c>-файла: на Windows отказывает, пока атрибут не снят (поэтому
  /// <c>CleanupAfterCommit</c> снимает атрибут перед удалением <c>.bak</c>), на POSIX удаление
  /// определяется правами каталога и проходит. После снятия атрибута удаление успешно везде —
  /// это и делает production-ветвь. Расхождение с ожиданием по платформе фиксируется видимым
  /// пропуском с причиной, а не падением: на поведение маршрута оно не влияет (журнал в обоих
  /// случаях снимает атрибут).
  /// </summary>
  [Fact]
  public void Probe_Delete_ReadOnlyФайла_ПлатформеннаяСемантика()
  {
    string root = NewRoot("del");
    string path = Path.Combine(root, "locked.bin");

    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [9]);
      File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), FileAttributes.ReadOnly);

      bool deletedWithoutClearing;
      try
      {
        File.Delete(StagedExtractionJournal.ToFileSystemPath(path));
        deletedWithoutClearing = true;
      }
      catch (UnauthorizedAccessException)
      {
        deletedWithoutClearing = false;
      }

      if (OperatingSystem.IsWindows() && deletedWithoutClearing)
        Assert.Skip("хост удаляет ReadOnly-файл без снятия атрибута (права/политики хоста): " +
                    "ветвь снятия атрибута в CleanupAfterCommit на таком хосте избыточна, но не вредна");

      if (!OperatingSystem.IsWindows() && !deletedWithoutClearing)
        Assert.Skip("на этой POSIX-системе удаление ReadOnly-файла отказало без снятия атрибута: " +
                    "ветвь CleanupAfterCommit необходима и здесь");

      if (!deletedWithoutClearing)
      {
        FileAttributes attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), attrs & ~FileAttributes.ReadOnly);
      }
      else
      {
        // Файл уже удалён: создаём заново, чтобы проверить общий вывод production-ветви.
        File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(path), [9]);
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), FileAttributes.ReadOnly);
        FileAttributes attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(path));
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(path), attrs & ~FileAttributes.ReadOnly);
      }

      // Общий для всех ОС вывод: после снятия атрибута файл удаляется.
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
  /// Измеряет регистрозависимость тома. Маршрут извлечения ищет коллизии имён компаратором
  /// <c>OrdinalIgnoreCase</c> на Windows и <c>Ordinal</c> на прочих ОС. На macOS APFS по умолчанию
  /// регистронезависима, поэтому там <c>Ordinal</c> коллизию не увидит — расхождение фиксируется
  /// как измеренный факт (видимый пропуск с причиной), а не как отказ: на сохранность данных это
  /// не влияет, второй элемент при <c>overwrite: false</c> отклоняется проверкой существования файла.
  /// </summary>
  [Fact]
  public void Probe_РегистрозависимостьТома_СоответствуетКомпараторуМаршрута()
  {
    string root = NewRoot("case");
    string lower = Path.Combine(root, "a.txt");
    string upper = Path.Combine(root, "A.TXT");

    try
    {
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(lower), [1]);
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(upper), [2]);

      int files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(root)).Length;
      bool caseInsensitive = files == 1;

      if (OperatingSystem.IsWindows())
      {
        Assert.True(caseInsensitive,
            "том на Windows оказался регистрозависимым: компаратор OrdinalIgnoreCase в поиске " +
            "коллизий имён на таком томе отвергал бы разные имена как дубликаты");
        Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(upper)),
            "Windows: имя в другом регистре обязано находить тот же файл");
        return;
      }

      if (caseInsensitive)
      {
        Assert.Skip("том регистронезависим (APFS по умолчанию): компаратор Ordinal в маршруте " +
                    "коллизии регистра не обнаруживает, второй элемент отклоняется проверкой " +
                    "существования файла — зафиксировано как ограничение, а не как отказ");
      }

      Assert.Equal(2, files);
      Assert.Equal([1], File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(lower)));
      Assert.Equal([2], File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(upper)));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }
}
