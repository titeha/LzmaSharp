using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Platform;

/// <summary>
/// <para>Фаза D (SEC-003), шаг D2a: test-only characterization и capability-probe.
/// Production-код НЕ изменён.</para>
/// <para>Два независимых блока:</para>
/// <list type="number">
/// <item>Capability-probe: <c>File.Move(source, dest)</c> без флага overwrite на существующий
/// файл. Документация .NET определяет только контракт «IOException: destFileName already exists
/// and overwrite is false», но НЕ определяет атомарность и реализацию на Unix. Тест измеряет
/// фактическое поведение: отказ с сохранением прежних байт или молчаливая замена (находка для
/// D2b).</item>
/// <item>Characterization гонки «файл появился между проверкой существования и публикацией»
/// в реальном маршруте извлечения ZIP при overwrite:false. Фиксация текущего поведения,
/// а не доказательство исправления.</item>
/// </list>
/// </summary>
public sealed class PublishRaceAndMoveProbeTests
{
  // ─────────────────────────────────────────────────────────────────────────────
  // Общие хелперы
  // ─────────────────────────────────────────────────────────────────────────────

  private static string NewTempRoot(string tag)
  {
    string root = Path.Combine(
        Path.GetTempPath(),
        $"lzsp-{tag}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  private static void TryDeleteFile(string path)
  {
    try
    {
      if (File.Exists(path))
        File.Delete(path);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // БЛОК 1. File.Move(overwrite:false) на существующий файл
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// <para>Capability-probe: <c>File.Move(source, dest)</c> без флага overwrite, когда dest уже
  /// существует. Измеряет фактическое поведение текущей ОС.</para>
  /// <para>Контракт .NET: при <c>overwrite=false</c> и существующем <c>destFileName</c> бросается
  /// <c>IOException</c>. Документация НЕ определяет атомарность операции и реализацию на POSIX
  /// (открытый вопрос (б) D1 Addendum). На Windows <c>File.Move</c> использует
  /// <c>MoveFileEx</c> без <c>MOVEFILE_REPLACE_EXISTING</c>, что атомарно отказывает. На POSIX
  /// .NET 8 предположительно использует <c>renameat2(RENAME_NOREPLACE)</c> при доступности
  /// (ядро 3.15+), но это не документировано.</para>
  /// <para>Допустимы два безопасных исхода:</para>
  /// <list type="number">
  /// <item>Брошено <c>IOException</c> (или производное) — целевой файл содержит прежние байты.
  /// Это ожидаемое поведение.</item>
  /// <item>Исключение НЕ брошено — целевой файл всё ещё содержит прежние байты (замена не
  /// произошла молча). Это консервативное поведение.</item>
  /// </list>
  /// <para>Если целевой файл оказался заменён содержимым источника — <c>Assert.Fail</c> с
  /// сообщением, что на этой ОС публикация переносом молча перезаписывает объект, появившийся
  /// после проверки (контракт <c>overwrite:false</c> нарушен). Это находка для D2b.</para>
  /// </summary>
  [Fact]
  public void FileMove_БезOverwrite_НаСуществующийФайл_СохраняетПрежниеБайты()
  {
    string root = NewTempRoot("move-probe");
    string source = Path.Combine(root, "source.bin");
    string target = Path.Combine(root, "target.bin");

    byte[] previousBytes = [1, 2, 3, 4, 5, 6, 7, 8];
    byte[] sourceBytes = [9, 10, 11, 12];

    try
    {
      File.WriteAllBytes(source, sourceBytes);
      File.WriteAllBytes(target, previousBytes);

      Exception? caught = null;
      try
      {
        File.Move(source, target, overwrite: false);
      }
      catch (Exception ex)
      {
        caught = ex;
      }

      // Инвариант сохранности: целевой файл содержит прежние байты.
      byte[] actualTarget = File.ReadAllBytes(target);

      if (caught is not null)
      {
        // Исход (i): брошено исключение. Целевой файл обязан содержать прежние байты.
        Assert.Equal(previousBytes, actualTarget);

        // Фиксируем тип исключения и ОС для отчёта (информативно, не утверждение).
        // На Windows: IOException (System.IO.IOException: Cannot create a file when that file already exists).
        // На Linux: IOException (System.IO.IOException: File exists).
        // На macOS: ожидается IOException, но тип и сообщение измеряются в CI.
        string osInfo = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsMacOS() ? "macOS"
            : OperatingSystem.IsLinux() ? "Linux"
            : "Unknown";
        string exceptionInfo = $"{caught.GetType().FullName}: {caught.Message}";

        // Дополнительная проверка: файл-источник всё ещё существует (перенос не произошёл).
        Assert.True(File.Exists(source),
            $"[{osInfo}] File.Move бросил {exceptionInfo}, но файл-источник исчез — " +
            "перенос произошёл частично, что нарушает контракт overwrite:false");
      }
      else
      {
        // Исход (ii): исключение НЕ брошено. Проверяем, что замена не произошла молча.
        if (!actualTarget.AsSpan().SequenceEqual(previousBytes))
        {
          // Целевой файл заменён содержимым источника — нарушение контракта overwrite:false.
          string osInfo = OperatingSystem.IsWindows() ? "Windows"
              : OperatingSystem.IsMacOS() ? "macOS"
              : OperatingSystem.IsLinux() ? "Linux"
              : "Unknown";
          Assert.Fail(
              $"[{osInfo}] File.Move(overwrite:false) молча перезаписал существующий файл: " +
              $"ожидались прежние байты [{string.Join(", ", previousBytes)}], " +
              $"получены [{string.Join(", ", actualTarget)}]. " +
              "На этой ОС публикация переносом молча заменяет объект, появившийся после проверки, " +
              "то есть контракт overwrite:false нарушен. Это находка для D2b, а не упавший тест.");
        }

        // Файл-источник исчез (перенос произошёл, но цель не заменена — странная семантика).
        // Это допустимо, если целевой файл содержит прежние байты (консервативное поведение).
      }
    }
    finally
    {
      TryDeleteFile(source);
      TryDeleteFile(target);
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // БЛОК 2. Characterization гонки публикации в маршруте ZIP
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Синхронный <see cref="IProgress{T}"/> для <c>currentFile</c>-колбэка.
  /// Выполняется на потоке извлечения (без контекста синхронизации), что позволяет
  /// детерминированно создать файл-конфликт в момент отчёта о первом элементе.
  /// </summary>
  private sealed class SyncFileProgress(Action<string> callback) : IProgress<string>
  {
    public void Report(string value) => callback(value);
  }

  /// <summary>
  /// <para>Characterization гонки «файл появился между проверкой существования и публикацией»
  /// в реальном маршруте извлечения ZIP при overwrite:false.</para>
  /// <para><b>Это фиксация текущего поведения, а не доказательство исправления.</b>
  /// Документированный код результата при конфликте «файл уже существует» — <c>InvalidData</c>
  /// (проверка <c>File.Exists</c> в <c>ZipExtractor.ExtractCore</c> строки 200-203).
  /// Фактическое поведение может отличаться (рассогласование диагностики — предмет D2b).</para>
  /// <para>Механизм детерминизма: архив содержит два элемента (A и B); при отчёте
  /// <c>currentFile</c> о первом элементе (A) в назначении создаётся файл с именем второго
  /// элемента (B) — имитация локального атакующего. Затем извлечение продолжается, и при
  /// обработке B обнаруживается конфликт.</para>
  /// <para>Инварианты сохранности:</para>
  /// <list type="bullet">
  /// <item>«Атакующий» файл B не перезаписан содержимым архива (или если перезаписан —
  /// <c>Assert.Fail</c> с явным сообщением).</item>
  /// <item>Sentinel-файл в назначении цел (если был создан до операции).</item>
  /// <item>После операции в назначении нет остатков .tmp/.bak.</item>
  /// </list>
  /// <para>Выбран маршрут ZIP (потоковый через <see cref="ZipExtractor.ExtractToDirectory"/>,
  /// фикстура через <see cref="ZipWriter.Build"/>): проще построить in-memory архив с двумя
  /// элементами и использовать <c>currentFile</c>-колбэк для детерминизма. Маршрут 7z не
  /// используется (объём шага ограничен).</para>
  /// </summary>
  [Fact]
  public void ZipExtract_ГонкаПубликации_ФайлПоявилсяМеждуПроверкойИпубликацией()
  {
    // Архив с двумя элементами: first.txt и second.txt.
    byte[] firstContent = Encoding.UTF8.GetBytes("first file content");
    byte[] secondContent = Encoding.UTF8.GetBytes("second file content");

    ZipWriterEntry[] entries =
    [
      new ZipWriterEntry("first.txt", firstContent),
      new ZipWriterEntry("second.txt", secondContent),
    ];

    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(entries, out byte[] archive));
    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] zipEntries));

    string dest = NewTempRoot("race-probe");
    string firstPath = Path.Combine(dest, "first.txt");
    string secondPath = Path.Combine(dest, "second.txt");

    // «Атакующий» файл: создаётся в назначении с именем второго элемента.
    byte[] attackerContent = Encoding.UTF8.GetBytes("ATTACKER FILE — не из архива");

    try
    {
      // Колбэк: при отчёте о первом элементе создаём файл с именем второго.
      var progress = new SyncFileProgress(currentFileName =>
      {
        if (currentFileName == "first.txt")
        {
          // Имитация локального атакующего: файл second.txt появляется в назначении
          // между проверкой существования (которая прошла для второго элемента до начала
          // извлечения) и публикацией (которая произойдёт после записи first.txt).
          File.WriteAllBytes(secondPath, attackerContent);
        }
      });

      ZipExtractResult result = ZipExtractor.ExtractToDirectory(
          zipEntries,
          dest,
          overwrite: false,
          currentFile: progress);

      // ─── Фиксация фактического поведения (characterization, не RED) ───

      // Инвариант 1: «атакующий» файл не перезаписан содержимым архива.
      // Если файл second.txt существует и содержит attackerContent — инвариант соблюдён.
      // Если файл содержит secondContent — нарушение сохранности (находка для D2b).
      if (File.Exists(secondPath))
      {
        byte[] actualSecond = File.ReadAllBytes(secondPath);
        if (!actualSecond.AsSpan().SequenceEqual(attackerContent))
        {
          Assert.Fail(
              "«Атакующий» файл second.txt перезаписан содержимым архива: " +
              $"ожидалось [{Encoding.UTF8.GetString(attackerContent)}], " +
              $"получено [{Encoding.UTF8.GetString(actualSecond)}]. " +
              "Нарушение сохранности: файл, появившийся в окне между проверкой и публикацией, " +
              "был перезаписан. Это находка для D2b.");
        }
      }

      // Инвариант 2: после операции в назначении нет остатков .tmp/.bak.
      string[] tmpFiles = Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories);
      string[] bakFiles = Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories);
      Assert.Empty(tmpFiles);
      Assert.Empty(bakFiles);

      // Фиксация фактического кода результата (characterization).
      // Ожидаемое поведение: InvalidData (конфликт «файл уже существует» при overwrite:false).
      // Фактическое поведение может отличаться (IOError или InternalError) — это рассогласование
      // диагностики, предмет D2b.
      //
      // На момент написания теста (шаг D2a) фактический код результата измеряется локальным
      // прогоном и фиксируется в отчёте. Утверждение НЕ является RED-проверкой: тест помечен
      // как characterization и документирует факт, а не требует исправления.
      //
      // Если результат InvalidData — это документированное поведение (проверка File.Exists
      // в ZipExtractor.ExtractCore строки 200-203).
      // Если результат IOError или InternalError — это рассогласование диагностики (предмет D2b).
      //
      // Утверждение: результат — один из допустимых кодов (InvalidData, IOError).
      // Для ZIP-маршрута: IOException мапится в IOError (ZipExtractor.cs строки 244-246).
      // InternalError существует только в SevenZipArchiveDecodeResult, не в ZipExtractResult.
      // Любой другой код (Ok, PathTooLong, InsufficientSpace, WrongPassword) — неожиданный.
      Assert.True(
          result is ZipExtractResult.InvalidData
          || result is ZipExtractResult.IOError,
          $"Неожиданный код результата при гонке публикации: {result}. " +
          "Ожидался один из: InvalidData (документированный), IOError.");

      // Инвариант 3: если результат != Ok, первый элемент не опубликован (откачен).
      // Если результат == Ok, первый элемент опубликован (но это означает, что гонка не
      // воспроизвелась или была проигнорирована).
      if (result != ZipExtractResult.Ok)
      {
        // Первый элемент не должен быть опубликован (откат).
        Assert.False(File.Exists(firstPath),
            $"Код результата {result}, но первый элемент first.txt опубликован в назначении. " +
            "Ожидался откат (первый элемент не должен существовать).");
      }
    }
    finally
    {
      TryDeleteTree(dest);
    }
  }
}
