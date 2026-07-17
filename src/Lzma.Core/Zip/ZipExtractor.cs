using Lzma.Core.SevenZip;

namespace Lzma.Core.Zip;

/// <summary>
/// <para>Пишет уже распакованные элементы ZIP-архива на диск в указанную папку.</para>
/// <para>
/// Переиспользует безопасную запись 7z-декодера: валидацию путей (запрет <c>..</c>, абсолютных
/// путей, выхода за пределы папки, device-имён Windows), проверку конфликтов с существующей ФС и
/// ОТКАТ всего созданного при сбое. Данные берутся из <see cref="ZipEntry.Bytes"/> (уже в памяти),
/// поэтому запись не потоковая.
/// </para>
/// </summary>
public static class ZipExtractor
{
  /// <summary>
  /// Записывает элементы <paramref name="entries"/> в <paramref name="destinationDirectory"/>.
  /// При сбое (небезопасный путь/конфликт/ошибка I/O) откатывает всё созданное — на диске
  /// «ничего не остаётся».
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
    if (entries is null || destinationDirectory is null)
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

      // Целевой путь должен быть каталогом, а не существующим файлом (или файлом-родителем).
      if (File.Exists(root))
        return ZipExtractResult.InvalidData;
      if (SevenZipArchiveDecoder.HasFileOnDirectoryPath(root, cmp))
        return ZipExtractResult.InvalidData;

      string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
          ? root
          : root + Path.DirectorySeparatorChar;

      // Заранее считаем и валидируем ВСЕ пути (защита от zip-slip + дубли, схлопывающиеся
      // в один путь на текущей ОС), чтобы не получить частичную распаковку.
      string[] fullPaths = new string[entries.Count];
      var seen = new HashSet<string>(pathComparer);
      for (int i = 0; i < entries.Count; i++)
      {
        if (!SevenZipArchiveDecoder.TryBuildSafePath(rootWithSep, entries[i].Name, cmp, out string fp))
          return ZipExtractResult.InvalidData;
        if (!seen.Add(fp))
          return ZipExtractResult.InvalidData;
        fullPaths[i] = fp;
      }

      var createdDirs = new List<string>();
      var createdFiles = new List<string>();
      bool committed = false;

      if (!Directory.Exists(root))
      {
        Directory.CreateDirectory(root);
        createdDirs.Add(root);
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
          createdDirs.Add(d);
        }
      }

      try
      {
        for (int i = 0; i < entries.Count; i++)
        {
          token.ThrowIfCancellationRequested();

          string fullPath = fullPaths[i];

          if (entries[i].IsDirectory)
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

            if (!SevenZipArchiveDecoder.TryPrepareExistingFileForOverwrite(fullPath))
              return ZipExtractResult.IOError;
          }

          CreateDirsTracked(dir);

          currentFile?.Report(entries[i].Name);

          createdFiles.Add(fullPath);
          File.WriteAllBytes(fullPath, entries[i].Bytes);
        }

        committed = true;
        return ZipExtractResult.Ok;
      }
      finally
      {
        // При любом сбое до commit — удаляем всё созданное (файлы, затем каталоги в обратном порядке).
        if (!committed)
        {
          for (int i = createdFiles.Count - 1; i >= 0; i--)
          {
            try { if (File.Exists(createdFiles[i])) File.Delete(createdFiles[i]); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
          }

          for (int i = createdDirs.Count - 1; i >= 0; i--)
          {
            try { if (Directory.Exists(createdDirs[i])) Directory.Delete(createdDirs[i], recursive: false); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
          }
        }
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
}
