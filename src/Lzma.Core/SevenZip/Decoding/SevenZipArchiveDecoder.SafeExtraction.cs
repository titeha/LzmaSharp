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
  /// Подготавливает существующий файл к перезаписи.
  /// Сейчас достаточно снять специальные атрибуты (в первую очередь ReadOnly),
  /// чтобы обычная запись поверх файла не падала по доступу.
  /// </summary>
  private static bool TryPrepareExistingFileForOverwrite(string fullPath)
  {
    try
    {
      File.SetAttributes(fullPath, FileAttributes.Normal);
      return true;
    }
    catch (IOException)
    {
      return false;
    }
    catch (UnauthorizedAccessException)
    {
      return false;
    }
    catch (ArgumentException)
    {
      return false;
    }
  }

  /// <summary>
  /// Строит безопасный путь назначения для элемента архива.
  /// Запрещает абсолютные пути, пустые сегменты, "."/"..", и выход за пределы root.
  /// </summary>
  private static bool TryBuildSafePath(
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
  /// </summary>
  private static bool HasFileOnPath(
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

      if (File.Exists(current))
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
  /// </summary>
  private static bool HasFileOnDirectoryPath(string fullDirectoryPath, StringComparison comparison)
  {
    string? current = fullDirectoryPath;

    while (current is not null)
    {
      if (File.Exists(current))
        return true;

      if (Directory.Exists(current))
        return false;

      string? parent = Path.GetDirectoryName(current);
      if (parent is null || string.Equals(parent, current, comparison))
        return false;

      current = parent;
    }

    return false;
  }
}
