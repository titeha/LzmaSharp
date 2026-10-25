namespace Lzma.Core.SevenZip;

// Хелперы безопасной записи на диск для ExtractToDirectory: валидация путей,
// device-имена Windows, проверка конфликтов с уже существующими файлами.
// См. основной файл SevenZipArchiveDecoder.cs.
public static partial class SevenZipArchiveDecoder
{
  private static bool IsValidFileTime(ulong raw)
  {
    if (raw > long.MaxValue)
      return false;

    try
    {
      _ = DateTime.FromFileTimeUtc((long)raw);
      return true;
    }
    catch (ArgumentOutOfRangeException)
    {
      return false;
    }
  }

  /// <summary>
  /// Строит безопасный путь назначения для элемента архива.
  /// Запрещает абсолютные пути, пустые сегменты, "."/"..", и выход за пределы root.
  /// </summary>
  internal static bool TryBuildSafePath(
    string rootWithSep,
    string entryName,
    StringComparison comparison,
    out string fullPath)
  {
    fullPath = string.Empty;

    if (string.IsNullOrEmpty(entryName))
      return false;

    if (entryName.Contains('\0'))
      return false;

    // Для Windows дополнительно режем "C:" и альтернативные потоки.
    if (OperatingSystem.IsWindows() && entryName.Contains(':'))
      return false;

    // Нормализуем разделители на '/', чтобы проще валидировать сегменты.
    // ВАЖНО: ничего не Trim()'им.
    // Иначе имя вроде "name " на Windows тихо превратится в "name",
    // что приведёт к неверному извлечению вместо InvalidData.
    string n = entryName.Replace('\\', '/');

    // Абсолютные пути не принимаем.
    if (n.StartsWith('/'))
      return false;

    // Убираем хвостовые '/', чтобы "dir/" и "dir" были эквивалентны.
    n = n.TrimEnd('/');

    if (n.Length == 0)
      return false;

    bool isWindows = OperatingSystem.IsWindows();

    // Валидируем сегменты: запрещаем пустые, "." и "..".
    // На Windows также запрещаем device-имена и сегменты,
    // оканчивающиеся пробелом или точкой.
    int segStart = 0;

    for (int i = 0; i <= n.Length; i++)
    {
      if (i != n.Length && n[i] != '/')
        continue;

      int segLen = i - segStart;
      if (segLen <= 0)
        return false;

      // "." ?
      if (segLen == 1 && n[segStart] == '.')
        return false;

      // ".." ?
      if (segLen == 2 && n[segStart] == '.' && n[segStart + 1] == '.')
        return false;

      if (isWindows)
      {
        ReadOnlySpan<char> segment = n.AsSpan(segStart, segLen);

        // Windows не допускает имена, оканчивающиеся пробелом или точкой.
        char last = segment[^1];
        if (last == ' ' || last == '.')
          return false;

        // Windows не допускает зарезервированные символы и управляющие коды
        // внутри имени файла/каталога.
        for (int j = 0; j < segment.Length; j++)
        {
          if (IsInvalidWindowsNameChar(segment[j]))
            return false;
        }

        // "NUL.txt" и "CON.tar.gz" тоже эквивалентны device-именам,
        // поэтому сравниваем базовое имя до первой точки.
        int dotIndex = segment.IndexOf('.');
        ReadOnlySpan<char> baseName = dotIndex >= 0 ? segment[..dotIndex] : segment;

        if (IsWindowsReservedDeviceName(baseName))
          return false;
      }

      segStart = i + 1;
    }

    // Конвертируем в системные разделители.
    string relative = n.Replace('/', Path.DirectorySeparatorChar);

    string combined = Path.GetFullPath(Path.Combine(rootWithSep, relative));

    // Защита от выхода за пределы root.
    if (!combined.StartsWith(rootWithSep, comparison))
      return false;

    fullPath = combined;
    return true;
  }

  /// <summary>
  /// Проверяет символы, недопустимые в Win32-именах файлов/каталогов.
  /// ':' и NUL здесь тоже считаем недопустимыми, хотя они уже режутся выше.
  /// </summary>
  private static bool IsInvalidWindowsNameChar(char c)
  {
    // U+0000..U+001F в обычных именах Windows запрещены.
    if (c < 32u)
      return true;

    return c == '<'
        || c == '>'
        || c == ':'
        || c == '"'
        || c == '/'
        || c == '\\'
        || c == '|'
        || c == '?'
        || c == '*';
  }

  /// <summary>
  /// Проверяет device-имена Windows:
  /// CON, PRN, AUX, NUL, COM1..COM9, LPT1..LPT9,
  /// а также варианты с superscript-цифрами COM¹/COM²/COM³, LPT¹/LPT²/LPT³.
  /// Сравнение выполняется без учёта регистра.
  /// </summary>
  private static bool IsWindowsReservedDeviceName(ReadOnlySpan<char> name)
  {
    if (name.Length == 0)
      return false;

    if (name.Equals("CON".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
        name.Equals("PRN".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
        name.Equals("AUX".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
        name.Equals("NUL".AsSpan(), StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    if (name.Length == 4)
    {
      ReadOnlySpan<char> prefix = name[..3];
      char suffix = name[3];

      if ((prefix.Equals("COM".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
           prefix.Equals("LPT".AsSpan(), StringComparison.OrdinalIgnoreCase)) &&
          IsWindowsReservedDeviceIndex(suffix))
      {
        return true;
      }
    }

    return false;
  }

  private static bool IsWindowsReservedDeviceIndex(char c)
  {
    return (uint)(c - '1') <= 8
        || c == '¹'
        || c == '²'
        || c == '³';
  }

  /// <summary>
  /// Проверяет, что на пути от root до fullPath нет сегментов,
  /// которые уже существуют как файл.
  /// Для каталогов includeSelf=true, чтобы поймать случай
  /// "в архиве каталог, а на диске по тому же пути уже файл".
  /// FS-вызовы переводятся на расширенную форму
  /// (<see cref="StagedExtractionJournal.ToFileSystemPath"/>), поэтому конфликт распознаётся
  /// и на путях от 259 символов независимо от long-path opt-in хоста (B5c); короткие пути,
  /// относительные пути и другие ОС не затрагиваются.
  /// </summary>
  internal static bool HasFileOnPath(
      string root,
      string fullPath,
      bool includeSelf,
      StringComparison comparison)
  {
    string? current = includeSelf ? fullPath : Path.GetDirectoryName(fullPath);

    while (current is not null)
    {
      if (string.Equals(current, root, comparison))
        return false;

      if (File.Exists(StagedExtractionJournal.ToFileSystemPath(current)))
        return true;

      string? parent = Path.GetDirectoryName(current);
      if (parent is null || string.Equals(parent, current, comparison))
        return false;

      current = parent;
    }

    return false;
  }

  /// <summary>
  /// Проверяет, что путь каталога не совпадает с файлом
  /// и что среди его родительских сегментов нет файлов.
  /// Если по пути уже встречается существующий каталог, дальше вверх можно не идти.
  /// FS-вызовы переводятся на расширенную форму
  /// (<see cref="StagedExtractionJournal.ToFileSystemPath"/>) — см. <see cref="HasFileOnPath"/>.
  /// </summary>
  internal static bool HasFileOnDirectoryPath(string fullDirectoryPath, StringComparison comparison)
  {
    string? current = fullDirectoryPath;

    while (current is not null)
    {
      if (File.Exists(StagedExtractionJournal.ToFileSystemPath(current)))
        return true;

      if (Directory.Exists(StagedExtractionJournal.ToFileSystemPath(current)))
        return false;

      string? parent = Path.GetDirectoryName(current);
      if (parent is null || string.Equals(parent, current, comparison))
        return false;

      current = parent;
    }

    return false;
  }

  /// <summary>
  /// <para>Проверяет цепочку каталогов от <paramref name="root"/> (НЕ включая) до
  /// <paramref name="fullPath"/> (включая) на наличие reparse-компонентов
  /// (symbolic links и junctions, флаг <see cref="FileAttributes.ReparsePoint"/>).</para>
  /// <para>Обход ПОКОМПОНЕНТНЫЙ сверху вниз, потому что <c>Path.GetFullPath</c>
  /// и <see cref="Directory.ResolveLinkTarget(string, bool)"/> с <c>returnFinalTarget:true</c>
  /// промежуточные ссылки не разрешают. Несуществующие компоненты пропускаются
  /// (их создаст маршрут извлечения).</para>
  /// <para>Проверенные компоненты кэшируются в <paramref name="checkedCache"/>
  /// с переданным <paramref name="comparer"/> — при извлечении нескольких элементов
  /// общие родительские каталоги не проверяются повторно.</para>
  /// <para>Что НЕ обнаруживает: окно между проверкой и открытием остаётся — ссылки,
  /// появившиеся после проверки, не ловятся. Исключения ввода-вывода
  /// (<see cref="IOException"/>, <see cref="UnauthorizedAccessException"/>,
  /// <see cref="NotSupportedException"/>, <see cref="ArgumentException"/>)
  /// подавляются (компонент считается не-reparse) — штатный отказ на таком пути
  /// произойдёт позже и будет корректно откачен журналом.</para>
  /// <para>Файловые вызовы идут через <see cref="StagedExtractionJournal.ToFileSystemPath"/>,
  /// поэтому охрана работает и на длинных путях.</para>
  /// </summary>
  /// <param name="root">Корень извлечения (сам не проверяется).</param>
  /// <param name="fullPath">Полный путь элемента (файла или каталога).</param>
  /// <param name="checkedCache">Кэш проверенных компонентов (может быть null — без кэша).</param>
  /// <param name="comparer">Компаратор путей операции (OrdinalIgnoreCase на Windows, Ordinal на
  /// прочих ОС): используется и для сравнения префикса, и (на стороне вызывающего) для кэша.</param>
  /// <returns><c>true</c>, если на пути найден reparse-компонент ИЛИ если атрибуты существующего
  /// компонента не удалось прочитать (fail-closed: операция отклоняется, а не продолжается вслепую).</returns>
  internal static bool HasReparsePointOnPath(
      string root,
      string fullPath,
      HashSet<string>? checkedCache,
      StringComparer comparer)
  {
    // Выделяем относительную часть пути от root до fullPath. Сравнение префикса берётся из
    // компаратора операции, чтобы не плодить второе определение платформенной семантики.
    StringComparison sc = ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase)
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    if (!fullPath.StartsWith(root, sc))
      return false;

    string relative = fullPath.Substring(root.Length)
        .TrimStart(Path.DirectorySeparatorChar)
        .TrimStart(Path.AltDirectorySeparatorChar);

    if (relative.Length == 0)
      return false;

    string[] parts = relative.Split(
        new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
        StringSplitOptions.RemoveEmptyEntries);

    string current = root;
    foreach (string part in parts)
    {
      current = Path.Combine(current, part);

      if (checkedCache is not null && checkedCache.Contains(current))
        continue;

      try
      {
        string fsPath = StagedExtractionJournal.ToFileSystemPath(current);
        if (!File.Exists(fsPath) && !Directory.Exists(fsPath))
          continue; // компонента ещё нет — его создаст маршрут

        FileAttributes attr;
        try
        {
          attr = File.GetAttributes(fsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
          // Fail-closed (MAJOR-1 кросс-семейного ревью D3c): компонент СУЩЕСТВУЕТ, но его атрибуты
          // прочитать не удалось. Прежний fail-open трактовал такой компонент как «не reparse»,
          // поэтому при ACL «запрет чтения атрибутов, но разрешены проход и запись» запись могла
          // уйти сквозь ссылку вне root без отказа. Цена fail-closed — возможен ложный отказ на
          // экзотических ФС, это осознанный выбор в пользу сохранности данных.
          return true;
        }

        if ((attr & FileAttributes.ReparsePoint) != 0)
          return true;

        checkedCache?.Add(current);
      }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
      catch (NotSupportedException) { }
      catch (ArgumentException) { }
      // Исключения выше относятся к построению пути и проверке существования (File.Exists и
      // Directory.Exists возвращают false вместо исключения при ошибке доступа): компонент при
      // этом считается отсутствующим, а штатный отказ маршрута на таком пути произойдёт позже
      // и будет откачен журналом.
    }

    return false;
  }
}
