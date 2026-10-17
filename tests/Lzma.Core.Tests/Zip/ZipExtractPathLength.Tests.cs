using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Тесты pre-flight проверки длины пути перед распаковкой ZIP (SEC-001, шаг B3b-2a).
/// Все тесты детерминированные: построение строк заданной длины, без обращения к реально
/// длинным путям на диске (кроме последнего интеграционного теста).
/// <para>
/// Честная граница: эти тесты проверяют контракт решения и отсутствие записи при превышении
/// длины пути, но НЕ доказывают порядок «проверка строго до первой мутации» — созданный
/// каталог удалился бы откатом в обоих случаях. Порядок обеспечен местом вставки в
/// <c>ExtractCore</c> (после проверки свободного места, до <c>Directory.CreateDirectory(root)</c>)
/// и подтверждён ревью.
/// </para>
/// </summary>
public sealed class ZipExtractPathLengthTests
{
  /// <summary>Создаёт уникальную временную папку (не создавая её на диске).</summary>
  private static string NewTempDir()
    => Path.Combine(Path.GetTempPath(), "lzs-pathlen-" + Guid.NewGuid().ToString("N"));

  private static byte[] Payload(string text) => Encoding.UTF8.GetBytes(text);

  /// <summary>Делегат записи для <c>ExtractCore</c>: пишет байты в staged-путь и считает вызовы.</summary>
  private static Func<int, string, ZipExtractResult> WriteToStaged(ZipEntry[] entries, int[] calls)
    => (i, stagedPath) =>
    {
      calls[0]++;
      File.WriteAllBytes(stagedPath, entries[i].Bytes);
      return ZipExtractResult.Ok;
    };

  /// <summary>
  /// Имя файла длиной 240 символов: 240 + 21 (суффикс стадии) = 261 &gt; 255 (лимит компонента).
  /// Выполняется на всех ОС (лимит компонента общий для NTFS и POSIX).
  /// </summary>
  [Fact]
  public void IsPathTooLongForStaging_ДлинныйКомпонентИмени_ВозвращаетTrue()
  {
    string longName = new string('a', 240);
    string fullPath = Path.Combine("C:\\root", longName);
    Assert.True(StagedExtractionJournal.IsPathTooLongForStaging(fullPath));
  }

  /// <summary>
  /// Короткий путь с обычным именем файла — проверка не должна срабатывать.
  /// </summary>
  [Fact]
  public void IsPathTooLongForStaging_ОбычныйПуть_ВозвращаетFalse()
  {
    string fullPath = Path.Combine("C:\\root", "short.txt");
    Assert.False(StagedExtractionJournal.IsPathTooLongForStaging(fullPath));
  }

  /// <summary>
  /// Граничное значение компонента: имя длиной ровно
  /// <c>MaxFileNameComponentLength - StagedSuffixLength</c> = 234 ⇒ <c>false</c>,
  /// длиной 235 ⇒ <c>true</c>. Проверка границы без магических чисел.
  /// </summary>
  [Fact]
  public void IsPathTooLongForStaging_ГраничноеЗначениеКомпонента_СоответствуетКонстанте()
  {
    int boundary = StagedExtractionJournal.MaxFileNameComponentLength
                 - StagedExtractionJournal.StagedSuffixLength;

    // Используем короткий корень, чтобы проверка полного пути не срабатывала раньше
    // проверки компонента: на Windows лимит полного пути (260) может вступить в игру
    // при длинном корне, и тест будет проверять не границу компонента, а границу пути.
    string root = "C:\\";

    // boundary = 234: 234 + 21 = 255, не > 255 → false (компонент в пределах).
    // Полный путь: 3 + 234 = 237, 237 + 21 = 258 < 260 (путь в пределах).
    string atBoundary = new string('b', boundary);
    string pathAt = Path.Combine(root, atBoundary);
    Assert.False(StagedExtractionJournal.IsPathTooLongForStaging(pathAt));

    // boundary + 1 = 235: 235 + 21 = 256, > 255 → true (компонент превышает лимит).
    // Полный путь: 3 + 235 = 238, 238 + 21 = 259 < 260 (путь в пределах, отказ по компоненту).
    string overBoundary = new string('c', boundary + 1);
    string pathOver = Path.Combine(root, overBoundary);
    Assert.True(StagedExtractionJournal.IsPathTooLongForStaging(pathOver));
  }

  /// <summary>
  /// Главный тест шага: при имени элемента длиной 300 символов (300 + 21 &gt; 255)
  /// <c>ExtractCore</c> возвращает <see cref="ZipExtractResult.PathTooLong"/>,
  /// каталог назначения НЕ создан, делегат записи не вызван ни разу.
  /// </summary>
  [Fact]
  public void ExtractCore_СлишкомДлинноеИмяЭлемента_ВозвращаетPathTooLong_ИНеПишетДанные()
  {
    string longName = new string('x', 300);
    ZipEntry[] entries = [new(longName, Payload("data"), IsDirectory: false)];
    int[] calls = [0];
    string dest = NewTempDir();
    try
    {
      ZipExtractResult result = ZipExtractor.ExtractCore(
          entries.Length,
          i => entries[i].Name,
          i => entries[i].IsDirectory,
          WriteToStaged(entries, calls),
          dest,
          overwrite: false,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      Assert.Equal(ZipExtractResult.PathTooLong, result);
      Assert.Equal(0, calls[0]);
      Assert.False(Directory.Exists(dest), "каталог назначения НЕ должен быть создан");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Защита правила «только в конец enum»: существующие значения не изменились, новое значение
  /// <see cref="ZipExtractResult.PathTooLong"/> == 5.
  /// </summary>
  [Fact]
  public void ZipExtractResult_НовоеЗначение_АддитивноИВКонце()
  {
    Assert.Equal(0, (int)ZipExtractResult.Ok);
    Assert.Equal(1, (int)ZipExtractResult.InvalidData);
    Assert.Equal(2, (int)ZipExtractResult.IOError);
    Assert.Equal(3, (int)ZipExtractResult.WrongPassword);
    Assert.Equal(4, (int)ZipExtractResult.InsufficientSpace);
    Assert.Equal(5, (int)ZipExtractResult.PathTooLong);
  }

  /// <summary>
  /// Интеграционная проверка: путь суммарной длиной ~300 символов с короткими компонентами
  /// (каждый &lt; 234 символов) извлекается успешно на текущей ОС.
  /// Если окружение не позволяет создать такой путь, тест использует <c>Assert.Skip</c>
  /// с явной причиной — тихий <c>return</c> запрещён.
  /// </summary>
  [Fact]
  public void ExtractToDirectory_ДлинныйПутьВПределахПлатформы_ИзвлекаетУспешно()
  {
    // Построение: корень temp + уникальная подпапка + длинный (но допустимый) компонент имени.
    string tempBase = Path.GetTempPath();
    string uniqueRoot = Path.Combine(tempBase, "lzs-pathlen-integ-" + Guid.NewGuid().ToString("N"));
    // Компонент имени длиной 200 символов: 200 + 21 = 221 < 255, в пределах лимита компонента.
    string longComponent = new string('d', 200);
    string dest = Path.Combine(uniqueRoot, longComponent);

    // Проверка: суммарный путь + суффикс стадии не превышает лимит платформы.
    string testFilePath = Path.Combine(dest, "file.txt");
    if (StagedExtractionJournal.IsPathTooLongForStaging(testFilePath))
    {
      Assert.Skip($"путь назначения '{testFilePath}' (длина {testFilePath.Length}) превышает " +
                  "принятый в реализации консервативный лимит (Windows: MAX_PATH 260 без long-path opt-in); " +
                  "интеграционный тест пропущен");
    }

    try
    {
      ZipEntry[] entries = [new("file.txt", Payload("long path integration"), IsDirectory: false)];
      int[] calls = [0];

      ZipExtractResult result = ZipExtractor.ExtractCore(
          entries.Length,
          i => entries[i].Name,
          i => entries[i].IsDirectory,
          WriteToStaged(entries, calls),
          dest,
          overwrite: false,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(1, calls[0]);
      Assert.Equal("long path integration",
          File.ReadAllText(Path.Combine(dest, "file.txt")));
    }
    finally
    {
      if (Directory.Exists(uniqueRoot)) Directory.Delete(uniqueRoot, recursive: true);
    }
  }

  /// <summary>
  /// Граница полного пути: проверяется ветвь сравнения <c>&gt;=</c> (лимит платформы включает
  /// завершающий ноль). Ожидания строятся от констант журнала, а не от магических чисел.
  /// </summary>
  [Fact]
  public void IsPathTooLong_ГраницаПолногоПути_СоответствуетЛимитуПлатформы()
  {
    int limit = OperatingSystem.IsWindows() ? StagedExtractionJournal.WindowsMaxPath
        : OperatingSystem.IsMacOS() ? StagedExtractionJournal.MacOsMaxPath
        : StagedExtractionJournal.PosixMaxPath;

    // extra = 0: проверяем именно ветвь полного пути, без суффикса стадии.
    string belowLimit = MakePath(limit - 1);   // limit - 1 + 0 <  limit ⇒ false
    string atLimit = MakePath(limit);          // limit     + 0 >= limit ⇒ true

    Assert.Equal(limit - 1, belowLimit.Length);
    Assert.Equal(limit, atLimit.Length);
    Assert.False(StagedExtractionJournal.IsPathTooLong(belowLimit, 0));
    Assert.True(StagedExtractionJournal.IsPathTooLong(atLimit, 0));
  }

  /// <summary>
  /// На POSIX лимиты <c>NAME_MAX</c>/<c>PATH_MAX</c> измеряются в байтах, поэтому имя из
  /// 128 кириллических символов (256 байт UTF-8) с суффиксом стадии (21) превышает лимит
  /// компонента, хотя в символах UTF-16 их всего 149. На Windows лимит считается
  /// в символах UTF-16, поэтому там то же имя допустимо.
  /// </summary>
  [Fact]
  public void IsPathTooLongForStaging_НеAsciiИмя_УчитываетБайтыНаPosix()
  {
    string name = new string('ф', 128);
    string fullPath = Path.Combine(OperatingSystem.IsWindows() ? @"C:\root" : "/root", name);

    bool expected = !OperatingSystem.IsWindows();
    Assert.Equal(expected, StagedExtractionJournal.IsPathTooLongForStaging(fullPath));
  }

  /// <summary>
  /// Смешанный список: первый элемент короткий, второй — слишком длинный. Ожидается
  /// <see cref="ZipExtractResult.PathTooLong"/> и ни одной записи (счётчик вызовов == 0),
  /// то есть отказ происходит до записи ПЕРВОГО элемента, а не после его публикации.
  /// </summary>
  [Fact]
  public void ExtractCore_СмешанныйСписок_ОтказДоЗаписиПервогоЭлемента()
  {
    ZipEntry[] entries =
    [
      new("short.txt", Payload("short"), IsDirectory: false),
      new(new string('y', 300), Payload("long"), IsDirectory: false),
    ];
    int[] calls = [0];
    string dest = NewTempDir();
    try
    {
      ZipExtractResult result = ZipExtractor.ExtractCore(
          entries.Length,
          i => entries[i].Name,
          i => entries[i].IsDirectory,
          WriteToStaged(entries, calls),
          dest,
          overwrite: false,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      Assert.Equal(ZipExtractResult.PathTooLong, result);
      Assert.Equal(0, calls[0]);
      Assert.False(Directory.Exists(dest), "каталог назначения НЕ должен быть создан");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Каталожный элемент со слишком длинным именем также отклоняется до первой мутации:
  /// без этой проверки <c>Directory.CreateDirectory</c> отказал бы на уровне ОС и результат
  /// был бы смаплен в общий <see cref="ZipExtractResult.IOError"/>.
  /// Суффикс стадии к именам каталогов не добавляется, поэтому проверка идёт с <c>extra = 0</c>.
  /// </summary>
  [Fact]
  public void ExtractCore_СлишкомДлинныйКаталог_ВозвращаетPathTooLong()
  {
    ZipEntry[] entries =
    [
      new(new string('z', 300), Array.Empty<byte>(), IsDirectory: true),
    ];
    int[] calls = [0];
    string dest = NewTempDir();
    try
    {
      ZipExtractResult result = ZipExtractor.ExtractCore(
          entries.Length,
          i => entries[i].Name,
          i => entries[i].IsDirectory,
          WriteToStaged(entries, calls),
          dest,
          overwrite: false,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      Assert.Equal(ZipExtractResult.PathTooLong, result);
      Assert.Equal(0, calls[0]);
      Assert.False(Directory.Exists(dest), "каталог назначения НЕ должен быть создан");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Строит путь ровно заданной длины из коротких компонентов (по 100 символов),
  /// чтобы проверка длины полного пути не перекрывалась проверкой компонента имени.
  /// Только ASCII: на POSIX длина в байтах совпадает с длиной в символах.
  /// </summary>
  private static string MakePath(int length)
  {
    var sb = new StringBuilder(OperatingSystem.IsWindows() ? @"C:\" : "/");
    while (sb.Length < length)
    {
      int chunk = Math.Min(100, length - sb.Length);
      sb.Append(new string('p', chunk));
      if (sb.Length < length)
        sb.Append(Path.DirectorySeparatorChar);
    }

    sb.Length = length;
    return sb.ToString();
  }
}
