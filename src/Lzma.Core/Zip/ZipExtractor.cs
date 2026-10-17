using Lzma.Core.SevenZip;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Пишет элементы ZIP-архива на диск в указанную папку.</para>
/// <para>
/// Переиспользует безопасную запись 7z-декодера: валидацию путей (запрет <c>..</c>, абсолютных
/// путей, выхода за пределы папки, device-имён Windows), проверку конфликтов с существующей ФС и
/// ОТКАТ всего созданного при сбое. Само содержимое пишется через делегат — для in-memory элементов
/// (<see cref="ExtractToDirectory(IReadOnlyList{ZipEntry}, string, bool, IProgress{string}, CancellationToken)"/>)
/// из уже распакованных байт, для потокового пути (<see cref="ZipStreamExtractor"/>) — прямо из файла.
/// </para>
/// </summary>
public static class ZipExtractor
{
  /// <summary>
  /// Записывает уже распакованные элементы <paramref name="entries"/> в
  /// <paramref name="destinationDirectory"/>. При сбое откатывает всё созданное.
  /// </summary>
  /// <param name="entries">Распакованные элементы (см. <see cref="ZipReader.Read"/>).</param>
  /// <param name="destinationDirectory">Целевая папка (создаётся при отсутствии).</param>
  /// <param name="overwrite">Разрешить перезапись уже существующих файлов.</param>
  /// <param name="currentFile">Необязательный отчёт об имени записываемого сейчас файла.</param>
  /// <param name="token">Токен отмены (кооперативная проверка между файлами).</param>
  public static ZipExtractResult ExtractToDirectory(
      IReadOnlyList<ZipEntry> entries,
      string destinationDirectory,
      bool overwrite = false,
      IProgress<string>? currentFile = null,
      CancellationToken token = default)
  {
    if (entries is null)
      return ZipExtractResult.InvalidData;

    return ExtractCore(
        entries.Count,
        i => entries[i].Name,
        i => entries[i].IsDirectory,
        (i, stagedPath) =>
        {
          File.WriteAllBytes(stagedPath, entries[i].Bytes);
          return ZipExtractResult.Ok;
        },
        destinationDirectory,
        overwrite,
        currentFile,
        token,
        i => entries[i].Bytes.LongLength);
  }

  /// <summary>
  /// Общее ядро извлечения: валидация путей, создание каталогов, staged-запись, публикация
  /// после проверки и откат при сбое. Данные каждого элемента пишет делегат
  /// <paramref name="writeAt"/> (index, staged-путь) → результат.
  /// </summary>
  internal static ZipExtractResult ExtractCore(
      int count,
      Func<int, string> nameAt,
      Func<int, bool> isDirAt,
      Func<int, string, ZipExtractResult> writeAt,
      string destinationDirectory,
      bool overwrite,
      IProgress<string>? currentFile,
      CancellationToken token,
      Func<int, long>? sizeAt = null)
  {
    if (destinationDirectory is null)
      return ZipExtractResult.InvalidData;

    try
    {
      string root = Path.GetFullPath(destinationDirectory);

      StringComparison cmp = OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;
      StringComparer pathComparer = OperatingSystem.IsWindows()
          ? StringComparer.OrdinalIgnoreCase
          : StringComparer.Ordinal;

      if (File.Exists(root))
        return ZipExtractResult.InvalidData;
      if (SevenZipArchiveDecoder.HasFileOnDirectoryPath(root, cmp))
        return ZipExtractResult.InvalidData;

      string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
          ? root
          : root + Path.DirectorySeparatorChar;

      // Заранее считаем и валидируем ВСЕ пути (защита от zip-slip + дубли, схлопывающиеся
      // в один путь на текущей ОС), чтобы не получить частичную распаковку.
      string[] fullPaths = new string[count];
      var seen = new HashSet<string>(pathComparer);
      for (int i = 0; i < count; i++)
      {
        if (!SevenZipArchiveDecoder.TryBuildSafePath(rootWithSep, nameAt(i), cmp, out string fp))
          return ZipExtractResult.InvalidData;
        if (!seen.Add(fp))
          return ZipExtractResult.InvalidData;
        fullPaths[i] = fp;
      }

      // Pre-flight: достаточно ли свободного места на томе назначения. Проверка выполняется
      // до первой мутации и носит best-effort характер (квоты, резерв ext4, параллельные
      // процессы), поэтому откат при IOException в середине записи остаётся обязательным.
      if (sizeAt is not null)
      {
        long required = 0;
        for (int i = 0; i < count; i++)
        {
          if (isDirAt(i))
            continue;

          // Размеры приходят из недоверенного заголовка архива: отрицательные значения
          // (Zip64 ≥ 2^63 после приведения к long) отбрасываются, а сумма насыщается,
          // чтобы переполнение не сделало проверку молча неэффективной.
          long size = sizeAt(i);
          if (size <= 0)
            continue;

          required = size > long.MaxValue - required ? long.MaxValue : required + size;
        }

        if (HasInsufficientSpace(root, required))
          return ZipExtractResult.InsufficientSpace;
      }

      // Pre-flight: длина пути. Проверка выполняется до первой мутации, чтобы не начинать
      // операцию, которая откажет на уровне ОС уже после публикации предыдущих элементов
      // (и вернула бы общий IOError вместо отличимого состояния).
      // Каталоги проверяются без суффикса стадии (он добавляется только к именам файлов),
      // потому что Directory.CreateDirectory на длинном пути тоже отказывает.
      if (StagedExtractionJournal.IsPathTooLong(root, 0))
        return ZipExtractResult.PathTooLong;

      for (int i = 0; i < count; i++)
      {
        int extra = isDirAt(i) ? 0 : StagedExtractionJournal.StagedSuffixLength;
        if (StagedExtractionJournal.IsPathTooLong(fullPaths[i], extra))
          return ZipExtractResult.PathTooLong;
      }

      var journal = new StagedExtractionJournal();
      bool committed = false;

      if (!Directory.Exists(root))
      {
        Directory.CreateDirectory(root);
        journal.TrackCreatedDirectory(root);
      }

      // Создаёт недостающие уровни каталога, запоминая каждый созданный (для отката).
      void CreateDirsTracked(string directory)
      {
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
          return;

        var missing = new Stack<string>();
        for (string? cur = directory; cur is not null && !Directory.Exists(cur); cur = Path.GetDirectoryName(cur))
          missing.Push(cur);

        while (missing.Count > 0)
        {
          string d = missing.Pop();
          Directory.CreateDirectory(d);
          journal.TrackCreatedDirectory(d);
        }
      }

      try
      {
        for (int i = 0; i < count; i++)
        {
          token.ThrowIfCancellationRequested();

          string fullPath = fullPaths[i];

          if (isDirAt(i))
          {
            if (SevenZipArchiveDecoder.HasFileOnPath(root, fullPath, includeSelf: true, cmp))
              return ZipExtractResult.InvalidData;

            CreateDirsTracked(fullPath);
            continue;
          }

          string? dir = Path.GetDirectoryName(fullPath);
          if (dir is null)
            return ZipExtractResult.InvalidData;

          // Родительские сегменты не должны конфликтовать с существующими файлами.
          if (SevenZipArchiveDecoder.HasFileOnPath(root, fullPath, includeSelf: false, cmp))
            return ZipExtractResult.InvalidData;

          // На месте файла уже каталог — конфликт.
          if (Directory.Exists(fullPath))
            return ZipExtractResult.InvalidData;

          if (File.Exists(fullPath))
          {
            if (!overwrite)
              return ZipExtractResult.InvalidData;
          }

          CreateDirsTracked(dir);

          currentFile?.Report(nameAt(i));

          // Staged-запись: делегат пишет во временный файл рядом с конечным путём.
          string stagedPath = journal.CreateStagedPath(fullPath);
          journal.TrackUnpublishedStaged(stagedPath);
          ZipExtractResult written = writeAt(i, stagedPath);
          if (written != ZipExtractResult.Ok)
            return written;

          // Публикация: поток стадии уже закрыт (using внутри делегата), переносим файл на место.
          journal.Publish(stagedPath, fullPath);
          journal.RemoveUnpublishedStaged(stagedPath);
        }

        // Все элементы успешно опубликованы — фиксируем.
        journal.Commit();
        committed = true;

        // Best-effort очистка backup-файлов; отказ не отменяет успех (семантика M4B).
        try { journal.CleanupAfterCommit(); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        return ZipExtractResult.Ok;
      }
      finally
      {
        // При любом сбое до commit — откат всей операции с восстановлением прежних байт.
        if (!committed)
          journal.Rollback();
      }
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (IOException)
    {
      return ZipExtractResult.IOError;
    }
    catch (UnauthorizedAccessException)
    {
      return ZipExtractResult.IOError;
    }
  }

  /// <summary>
  /// Best-effort измерение свободного места на томе, содержащем <paramref name="root"/>.
  /// Том выбирается как запись <see cref="DriveInfo.GetDrives"/> с НАИБОЛЕЕ ДЛИННЫМ совпадением
  /// префикса пути: на Windows это буква диска, на POSIX — точка монтирования. Поэтому назначение
  /// на смонтированном томе (внешний диск, NAS, отдельный /home) измеряется по своему тому,
  /// а не по корневой ФС (лексический корень <c>Path.GetPathRoot</c> на POSIX всегда «/»).
  /// Если совпадения нет (например, UNC-путь) или том не готов, возвращается <c>false</c> —
  /// проверка места пропускается и операция не блокируется.
  /// </summary>
  private static bool TryGetAvailableFreeSpace(string root, out long availableFreeSpace)
  {
    availableFreeSpace = 0;
    try
    {
      if (!TryResolveVolumeRoot(root, out string volumeRoot))
        return false;

      var drive = new DriveInfo(volumeRoot);
      if (!drive.IsReady)
        return false;

      availableFreeSpace = drive.AvailableFreeSpace;
      return true;
    }
    catch (ArgumentException) { return false; }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
    catch (NotSupportedException) { return false; }
  }

  /// <summary>
  /// Определяет корень тома, содержащего <paramref name="path"/>: на Windows это буква диска
  /// (<c>C:\</c>), на POSIX — точка монтирования (<c>/</c>, <c>/media/user/USB</c>). Выбирается
  /// запись <see cref="DriveInfo.GetDrives"/> с наиболее длинным совпадением префикса пути;
  /// при отсутствии совпадения (например, UNC) возвращает <c>false</c>.
  /// Метод internal: тесты подтверждают разрешение тома на каждой ОС CI, чтобы выбор тома
  /// не оставался недоказанным допущением.
  /// </summary>
  internal static bool TryResolveVolumeRoot(string path, out string volumeRoot)
  {
    volumeRoot = string.Empty;
    try
    {
      StringComparison cmp = OperatingSystem.IsWindows()
          ? StringComparison.OrdinalIgnoreCase
          : StringComparison.Ordinal;

      string? best = null;
      foreach (DriveInfo drive in DriveInfo.GetDrives())
      {
        string name = drive.Name;
        if (string.IsNullOrEmpty(name) || !IsSameOrUnderVolume(path, name, cmp))
          continue;

        if (best is null || name.Length > best.Length)
          best = name;
      }

      if (best is null)
        return false;

      volumeRoot = best;
      return true;
    }
    catch (ArgumentException) { return false; }
    catch (IOException) { return false; }
    catch (UnauthorizedAccessException) { return false; }
    catch (NotSupportedException) { return false; }
  }

  /// <summary>
  /// Проверяет, что <paramref name="path"/> совпадает с корнем тома <paramref name="volumeRoot"/>
  /// или находится под ним. Сравнение выполняется по границе разделителя, чтобы том
  /// <c>/media/user/USB</c> не совпал с путём <c>/media/user/USBdata</c>.
  /// </summary>
  private static bool IsSameOrUnderVolume(string path, string volumeRoot, StringComparison cmp)
  {
    string normalized = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Корень «/» на POSIX после нормализации становится пустой строкой: любой абсолютный путь под ним.
    if (normalized.Length == 0)
      return true;

    if (path.Equals(normalized, cmp))
      return true;

    return path.StartsWith(normalized + Path.DirectorySeparatorChar, cmp);
  }

  /// <summary>
  /// Решение о нехватке места: <c>true</c>, если измерение доступно и свободного места меньше
  /// требуемого. Если измерить не удалось — <c>false</c> (операцию не блокируем).
  /// </summary>
  internal static bool HasInsufficientSpace(string root, long requiredBytes)
      => TryGetAvailableFreeSpace(root, out long available) && available < requiredBytes;
}
