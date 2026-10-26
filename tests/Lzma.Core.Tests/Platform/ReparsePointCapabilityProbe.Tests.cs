using System.Diagnostics;

namespace Lzma.Core.Tests.Platform;

/// <summary>
/// <para>Фаза D (SEC-003), шаг D3a: capability-probe ссылок как объектов ФС (symbolic link и junction).
/// <b>Production-код не изменён</b>; probe измеряет факты, от которых зависит шаг D3c
/// (обнаружение reparse-компонентов в цепочке пути назначения).</para>
/// <para>Ограничения, зафиксированные владельцем (D1 Addendum §2): junction создаётся
/// <b>внешним процессом</b> (<c>cmd /d /c mklink /J</c>), потому что без P/Invoke он недоступен;
/// обнаружение в production — по <b>общему флагу</b> <see cref="FileAttributes.ReparsePoint"/>
/// без различения типов ссылок. P/Invoke и unsafe запрещены, поэтому тест остаётся 100% managed:
/// <c>Directory.CreateSymbolicLink</c> + <c>Process</c>.</para>
/// <para>Три группы измерений (по группам — отдельные независимые тесты):</para>
/// <list type="number">
/// <item><b>Группа 1 — создание ссылки на каталог:</b> symbolic link managed-API на каждой ОС;
/// junction внешним процессом только на Windows. Каждая созданная ссылка удаляется
/// <c>Directory.Delete(link, recursive: false)</c>, то есть удаляется САМА ссылка, и тест утверждает,
/// что цель осталась цела (байты маркера) — без этого нельзя убирать фикстуру D3b/D3c.</item>
/// <item><b>Группа 2 — обнаружение (главное для D3c):</b> <c>File.GetAttributes(link)</c> обязан
/// нести флаг <c>ReparsePoint</c>; классификация объекта (<c>Directory.Exists</c> = true,
/// <c>File.Exists</c> = false), чтобы D3c не спутал ссылку-каталог с файлом;
/// <c>Directory.ResolveLinkTarget(link, returnFinalTarget: false)</c> — не-null и цель = ожидаемый
/// путь (документация обещает разрешение «Symbolic links and junctions», см. D1 Addendum §2.1).
/// Контроль ложных срабатываний: у обычного каталога цели флага <c>ReparsePoint</c> быть не должно.</item>
/// <item><b>Группа 3 — семантика записи сквозь ссылку (формулировка риска для D3c):</b> ссылка
/// внутри «каталога назначения» ведёт во внешний sentinel; запись по пути
/// <c>root/&lt;ссылка&gt;/file.txt</c> физически создаёт файл В SENTINEL, то есть вне root. Это и
/// есть атака профиля B, которую D3c обязан отклонять; тест характеризует поведение ОС, а не
/// дефект production-кода.</item>
/// </list>
/// <para>Политика жёсткости (образец — <c>PlatformCapabilityProbeTests</c>): падение только там,
/// где расхождение блокирует шаг D3c (флаг <c>ReparsePoint</c> не установлен на ссылке; ссылка на
/// каталог не читается как каталог; обычный каталог помечен как reparse-точка). Невозможность
/// воспроизвести сценарий в данном окружении (нет привилегии на symbolic link, mklink отказал,
/// <c>ResolveLinkTarget</c> дал null или другую запись пути) — видимый <c>Assert.Skip</c> с
/// измеренным значением и причиной; тихий <c>return</c> из <c>[Fact]</c> запрещён
/// (<c>03_TEST_QUALITY.md</c> §7).</para>
/// <para>Честные границы probe: (1) гонка «проверка → открытие» (TOCTOU) здесь НЕ измеряется —
/// измеряется только обнаружение заранее созданной ссылки; (2) промежуточные ссылки в цепочке
/// (link как не-конечный компонент) здесь не проверяются — по измеренному факту D1 Addendum §2.2
/// разрешает их только покомпонентным обходом, это предмет D3c; (3) junction существует только на
/// Windows, на Linux/macOS соответствующие тесты пропускаются с причиной; (4) пути строятся от
/// канонизированного <see cref="Path.GetTempPath"/> и остаются короткими, поэтому расширенная форма
/// <c>StagedExtractionJournal.ToFileSystemPath</c> для этих замеров не нужна (на коротких путях она
/// возвращает вход без изменения).</para>
/// </summary>
public sealed class ReparsePointCapabilityProbeTests
{
  // ─────────────────────────────────────────────────────────────────────────────
  // Общие хелперы
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>Таймаут дочернего <c>cmd.exe</c>: <c>mklink</c> — встроенная команда, отрабатывает
  /// мгновенно; таймаут нужен, чтобы зависший внешний процесс не остановил тестовый прогон.</summary>
  private const int MkLinkTimeoutMilliseconds = 30_000;

  /// <summary>Дополнительный потолок ожидания дочитывания перенаправленных потоков после выхода
  /// процесса: диагностика best-effort, она не должна вешать тест.</summary>
  private const int StreamDrainTimeoutMilliseconds = 2_000;

  /// <summary>Ограничение длины диагностического текста (stderr/stdout) в причинах отказа/пропуска.</summary>
  private const int DiagnosticTextCeiling = 400;

  private static string OsName() =>
      OperatingSystem.IsWindows() ? "Windows"
      : OperatingSystem.IsMacOS() ? "macOS"
      : OperatingSystem.IsLinux() ? "Linux"
      : "Unknown";

  /// <summary>
  /// Канонический temp-корень. На macOS <c>Path.GetTempPath()</c> возвращает <c>/var/folders/…</c>,
  /// а <c>/var</c> — symlink на <c>/private/var</c> (измерено в <c>PlatformCapabilityProbeTests</c>),
  /// поэтому ссылка хранит путь в одном написании, а ядро видит другой. <c>ResolveLinkTarget</c>
  /// разрешает только КОНЕЧНЫЙ компонент, поэтому идём по компонентам сверху вниз.
  /// </summary>
  private static string Canonicalize(string path)
  {
    string root = Path.GetPathRoot(path) ?? Path.DirectorySeparatorChar.ToString();
    string result = root;

    foreach (string part in path.Substring(root.Length).Split(
                 Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
    {
      string candidate = Path.Combine(result, part);
      try
      {
        result = Directory.ResolveLinkTarget(candidate, returnFinalTarget: true)?.FullName ?? candidate;
      }
      catch (IOException)
      {
        result = candidate; // компонент не читается — оставляем как есть
      }
      catch (UnauthorizedAccessException)
      {
        result = candidate;
      }
    }

    return result;
  }

  private static string NewRoot(string tag)
  {
    string canonical = Canonicalize(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    string root = Path.Combine(canonical, $"lzsp-{tag}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(root);
    return root;
  }

  /// <summary>Сокращает диагностический текст, чтобы причина пропуска/отказа читалась в CI.</summary>
  private static string Diagnostic(string? text)
  {
    string oneLine = (text ?? string.Empty)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace('\n', ' ');
    return oneLine.Length <= DiagnosticTextCeiling
        ? oneLine
        : oneLine.Substring(0, DiagnosticTextCeiling) + "…";
  }

  /// <summary>
  /// Пробует создать symbolic link на каталог managed-API <c>Directory.CreateSymbolicLink</c>.
  /// Возвращает <c>null</c> при успехе либо текст с типом и сообщением исключения — он идёт в
  /// причину видимого <c>Assert.Skip</c>. На Windows вероятен отказ без привилегии
  /// SeCreateSymbolicLink / Developer Mode; D1 Addendum §2.3 помечает этот факт как неизмеренный.
  /// </summary>
  private static string? TryCreateDirectorySymlink(string linkPath, string targetPath)
  {
    try
    {
      Directory.CreateSymbolicLink(linkPath, targetPath);
      return null;
    }
    catch (Exception ex) when (ex is UnauthorizedAccessException
                               or IOException
                               or ArgumentException
                               or NotSupportedException)
    {
      return $"{ex.GetType().Name}: {ex.Message}";
    }
  }

  /// <summary>
  /// Читает перенаправленный поток с ограничением по времени: после выхода процесса pipe обычно
  /// уже закрыт, но зависший потомок не должен останавливать прогон — тогда вместо диагностики
  /// возвращается отметка о неполном чтении.
  /// </summary>
  private static string Drain(Task<string> reader, string label)
  {
    try
    {
      return reader.Wait(StreamDrainTimeoutMilliseconds) ? reader.Result : $"<{label} не дочитан>";
    }
    catch (Exception ex)
    {
      return $"<{label}: {ex.GetType().Name}>";
    }
  }

  /// <summary>
  /// Создаёт junction ВНЕШНИМ ПРОЦЕССОМ: <c>cmd /d /c mklink /J "&lt;link&gt;" "&lt;target&gt;"</c>.
  /// Решение владельца (D1 Addendum §2 (1)): без P/Invoke junction недоступен, различение типов
  /// reparse-точек тоже, поэтому production проверяет общий флаг <c>ReparsePoint</c>.
  /// Возвращает <c>null</c> при успехе либо измеренную причину (ExitCode, stderr/stdout, наличие
  /// объекта) для видимого <c>Assert.Skip</c>.
  /// </summary>
  private static string? TryCreateJunction(string linkPath, string targetPath)
  {
    string shell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";

    var startInfo = new ProcessStartInfo(shell)
    {
      UseShellExecute = false,
      CreateNoWindow = true,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
    };

    // Аргументы списком: кавычки для путей с пробелами собирает ProcessStartInfo, а не строка.
    startInfo.ArgumentList.Add("/d");
    startInfo.ArgumentList.Add("/c");
    startInfo.ArgumentList.Add("mklink");
    startInfo.ArgumentList.Add("/J");
    startInfo.ArgumentList.Add(linkPath);
    startInfo.ArgumentList.Add(targetPath);

    using var process = new Process { StartInfo = startInfo };

    try
    {
      process.Start();
    }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                               or InvalidOperationException
                               or UnauthorizedAccessException)
    {
      return $"не удалось запустить \"{shell}\": {ex.GetType().Name}: {ex.Message}";
    }

    // Читаем асинхронно: синхронный ReadToEnd до WaitForExit мог бы заблокироваться, а таймаут
    // тогда не сработал бы.
    Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
    Task<string> stderrTask = process.StandardError.ReadToEndAsync();

    if (!process.WaitForExit(MkLinkTimeoutMilliseconds))
    {
      TryKill(process);
      return $"\"{shell} /d /c mklink /J\" не завершился за {MkLinkTimeoutMilliseconds} мс — " +
             "процесс снят (Kill), замер junction невозможен в этом окружении";
    }

    string stdout = Drain(stdoutTask, "stdout");
    string stderr = Drain(stderrTask, "stderr");

    if (process.ExitCode != 0)
      return $"ExitCode={process.ExitCode}, stdout=\"{Diagnostic(stdout)}\", stderr=\"{Diagnostic(stderr)}\"";

    if (!Directory.Exists(linkPath))
      return $"ExitCode=0, но объект ссылки не появился: stdout=\"{Diagnostic(stdout)}\", " +
             $"stderr=\"{Diagnostic(stderr)}\"";

    return null;
  }

  private static void TryKill(Process process)
  {
    try
    {
      if (!process.HasExited)
        process.Kill(entireProcessTree: true);
    }
    catch (InvalidOperationException) { /* процесс уже завершён */ }
    catch (System.ComponentModel.Win32Exception) { /* best-effort */ }
  }

  /// <summary>
  /// Удаляет САМУ ссылку (не цель): <c>Directory.Delete(link, recursive: false)</c> для ссылки на
  /// каталог, <c>File.Delete</c> — если объект всё же читается как файл. Возвращает <c>null</c> при
  /// успехе либо текст исключения как измеренную причину (привилегии/антивирус на хосте).
  /// </summary>
  private static string? TryDeleteLink(string linkPath)
  {
    try
    {
      if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
        return null; // объекта уже нет — повторная очистка не нужна

      if ((File.GetAttributes(linkPath) & FileAttributes.Directory) != 0)
        Directory.Delete(linkPath, recursive: false);
      else
        File.Delete(linkPath);

      return null;
    }
    catch (Exception ex) when (ex is IOException
                               or UnauthorizedAccessException
                               or ArgumentException
                               or NotSupportedException)
    {
      return $"{ex.GetType().Name}: {ex.Message}";
    }
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

  /// <summary>
  /// Удаляет дерево. Вызывается ПОСЛЕ <see cref="TryDeleteLink"/>: рекурсия не должна
  /// добраться до внешнего sentinel.
  /// </summary>
  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  /// <summary>
  /// Общий флаг reparse-точки — ровно тот managed-API (<c>File.GetAttributes</c>), который выбран
  /// для production-обнаружения в D3c.
  /// </summary>
  private static bool HasReparsePoint(string path) =>
      (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

  // ─────────────────────────────────────────────────────────────────────────────
  // Группа 2: обнаружение заранее созданной ссылки (общий блок для обоих типов)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Измеряет обнаружимость ссылки на каталог — то, от чего зависит механизм D3c.
  /// <para>ЖЁСТКИЕ проверки (блокирующие находки, падают осознанно):</para>
  /// <list type="bullet">
  /// <item>флаг <c>ReparsePoint</c> на ссылке установлен — иначе выбранный владельцем механизм
  /// (общий флаг без различения типов) на этой ОС/типе ссылки не работает;</item>
  /// <item><c>Directory.Exists(link)</c> = true и <c>File.Exists(link)</c> = false — иначе D3c
  /// классифицировал бы ссылку-каталог не как каталог (или, наоборот, обычный файл-конфликт
  /// перекрывал бы обход цепочки);</item>
  /// <item>на обычном каталоге цели флага <c>ReparsePoint</c> НЕТ — иначе проверка давала бы
  /// ложные отказы на легитимных каталогах назначения.</item>
  /// </list>
  /// <para>КОНСЕРВАТИВНОЕ расхождение (видимый <c>Assert.Skip</c> с измеренным значением):
  /// <c>ResolveLinkTarget</c> вернул null или цель в другом написании. Механизм обнаружения по
  /// флагу от этого не зависит, но D3c не сможет использовать разрешение цели как данность —
  /// факт фиксируется явно, потому что документация (D1 Addendum §2.1) обещает поддержку обоих
  /// типов ссылок.</para>
  /// </summary>
  private static void MeasureLinkDetection(string linkPath, string targetPath, string kind)
  {
    string os = OsName();
    FileAttributes linkAttributes = File.GetAttributes(linkPath);

    if (!HasReparsePoint(linkPath))
    {
      Assert.Fail($"[{os}] {kind}: File.GetAttributes вернул \"{linkAttributes}\" БЕЗ флага " +
                  "ReparsePoint — производственный механизм обнаружения по общему флагу " +
                  "(решение владельца, D1 Addendum §2 (1)) на этой ОС и для этого типа ссылки НЕ " +
                  "работает: шаг D3c обязан это учесть (блокирующая находка: записывать, что «ссылки " +
                  "обнаруживаются» на такой платформе нельзя)");
    }

    bool directoryExists = Directory.Exists(linkPath);
    bool fileExists = File.Exists(linkPath);

    Assert.True(directoryExists,
        $"[{os}] {kind}: флаг ReparsePoint установлен ({linkAttributes}), но Directory.Exists(link) = false " +
        "при ссылке НА КАТАЛОГ — D3c спутал бы компонент цепочки с несуществующим объектом " +
        "(ссылка читается как отсутствует): фикстуры D3b и обход цепочки на этой платформе " +
        "строятся иначе");

    Assert.False(fileExists,
        $"[{os}] {kind}: File.Exists(link) = true при ссылке НА КАТАЛОГ ({linkAttributes}) — " +
        "D3c и охрана overwrite=false приняли бы ссылку-каталог за файл: классификация " +
        "компонента цепочки на этой платформе требует правки");

    FileAttributes targetAttributes = File.GetAttributes(targetPath);
    if (HasReparsePoint(targetPath))
    {
      Assert.Fail($"[{os}] контроль ложных срабатываний: обычный каталог цели имеет флаг " +
                  $"ReparsePoint (\"{targetAttributes}\") — проверка по общему флагу отклоняла бы " +
                  "легитимные каталоги назначения (блокирующая находка для D3c)");
    }

    FileSystemInfo? resolved = Directory.ResolveLinkTarget(linkPath, returnFinalTarget: false);

    if (resolved is null)
    {
      Assert.Skip($"[{os}] {kind}: ReparsePoint установлен ({linkAttributes}), но " +
                  "Directory.ResolveLinkTarget(returnFinalTarget: false) вернул null — документация " +
                  "обещает разрешение «Symbolic links and junctions» (D1 Addendum §2.1). " +
                  "Обнаружение по флагу это НЕ ломает, но D3c обязан не полагаться на " +
                  "ResolveLinkTarget при определении направления ссылки (вне root или нет)");
    }

    string expected = Path.GetFullPath(targetPath);
    StringComparison comparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    if (!string.Equals(resolved.FullName, expected, comparison))
    {
      Assert.Skip($"[{os}] {kind}: ResolveLinkTarget(returnFinalTarget: false) вернул цель " +
                  $"\"{resolved.FullName}\", ожидалось \"{expected}\" (сравнение: {comparison}) — " +
                  "расхождение написания; обнаружение по флагу не затронуты, но D3c обязан " +
                  "сравнивать цели через нормализацию, а не прямым равенством");
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Группа 1 + 2: symbolic link
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// <para>Symbolic link на каталог: создаётся managed-API <c>Directory.CreateSymbolicLink</c>,
  /// обнаруживается по общему флагу <c>ReparsePoint</c>, удаляется вместе с САМОЙ ссылкой, а цель
  /// остаётся целой.</para>
  /// <para>Если окружение не дало создать ссылку (на Windows — привилегия SeCreateSymbolicLink /
  /// Developer Mode; см. D1 Addendum §2.3, где этот факт помечен как неизмеренный), тест пропускается
  /// видимым <c>Assert.Skip</c> с типом и сообщением исключения — группа 2 для этого типа ссылки
  /// остаётся неизмеренной, и это честно зафиксировано, а не «passed».</para>
  /// </summary>
  [Fact]
  public void Probe_SymbolicLinkКаталога_ОбнаруживаетсяПоФлагуReparsePoint()
  {
    string root = NewRoot("sym-probe");
    string target = Path.Combine(root, "target");
    string link = Path.Combine(root, "link");
    string marker = Path.Combine(target, "marker.bin");
    byte[] markerBytes = [1, 2, 3, 4, 5];

    try
    {
      Directory.CreateDirectory(target);
      File.WriteAllBytes(marker, markerBytes);

      string? creation = TryCreateDirectorySymlink(link, target);
      if (creation is not null)
      {
        Assert.Skip($"[{OsName()}] окружение не позволило создать symbolic link через " +
                    $"Directory.CreateSymbolicLink: {creation} — на Windows это означает отсутствие " +
                    "привилегии SeCreateSymbolicLink при невключённом Developer Mode, на Linux и macOS " +
                    "отказ был бы находкой. Ссылка не создана → обнаружение для этого типа не измерено");
      }

      MeasureLinkDetection(link, target, "symbolic link");

      // Группа 1: удаляем САМУ ссылку (recursive: false) и утверждаем, что цель цела.
      string? removal = TryDeleteLink(link);
      if (removal is not null)
      {
        Assert.Skip($"[{OsName()}] symbolic link не удалось удалить ({removal}) — утверждение «удаление " +
                    "ссылки не трогает цель» на этом хосте не измерено; очистка повторится в finally");
      }

      Assert.False(Directory.Exists(link),
          $"[{OsName()}] после успешного Directory.Delete(link, recursive: false) ссылка всё ещё " +
          "видима как каталог — очистка фикстуры D3b/D3c не даст предсказуемого состояния");
      Assert.True(Directory.Exists(target),
          $"[{OsName()}] удаление ссылки удалило и ЦЕЛЬ (каталог \"{target}\" исчез) — так убирать " +
          "фикстуру нельзя: были бы потеряны данные вне области, созданной тестом");
      Assert.Equal(markerBytes, File.ReadAllBytes(marker));
    }
    finally
    {
      // Порядок: сначала ссылка, затем дерево (рекурсия не должна идти сквозь ссылку).
      TryDeleteLink(link);
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Группа 1 + 2: junction (только Windows, внешний процесс)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// <para>Junction на каталог: создаётся ВНЕШНИМ ПРОЦЕССОМ <c>cmd /d /c mklink /J</c> (решение
  /// владельца — без P/Invoke junction недоступен), затем измеряется тем же общим блоком, что и
  /// symbolic link: общий флаг <c>ReparsePoint</c> обязан обнаруживать оба типа ссылок без их
  /// различения.</para>
  /// <para>Вне Windows тест пропускается с причиной «junction — объект Windows». Если mklink отказал
  /// (права/политики хоста) — видимый <c>Assert.Skip</c> с ExitCode и содержимым stderr/stdout.</para>
  /// </summary>
  [Fact]
  public void Probe_JunctionКаталога_ОбнаруживаетсяПоФлагуReparsePoint()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip($"[{OsName()}] junction — объект Windows (NTFS reparse point, создаётся встроенной " +
                  "командой cmd mklink /J); managed API его не создаёт, P/Invoke запрещён владельцем " +
                  "→ сценарий на этой ОС не воспроизводится");
    }

    string root = NewRoot("jun-probe");
    string target = Path.Combine(root, "target");
    string link = Path.Combine(root, "link");
    string marker = Path.Combine(target, "marker.bin");
    byte[] markerBytes = [6, 7, 8, 9];

    try
    {
      Directory.CreateDirectory(target);
      File.WriteAllBytes(marker, markerBytes);

      string? creation = TryCreateJunction(link, target);
      if (creation is not null)
      {
        Assert.Skip($"[Windows] внешний процесс не создал junction: {creation} — права/политики хоста " +
                    "(mklink /J обычно не требует привилегий). Ссылка не создана → обнаружение " +
                    "для junction не измерено");
      }

      MeasureLinkDetection(link, target, "junction");

      string? removal = TryDeleteLink(link);
      if (removal is not null)
      {
        Assert.Skip($"[Windows] junction не удалось удалить ({removal}) — утверждение «удаление ссылки " +
                    "не трогает цель» для junction не измерено; очистка повторится в finally");
      }

      Assert.False(Directory.Exists(link),
          "[Windows] после Directory.Delete(link, recursive: false) junction всё ещё видима как каталог");
      Assert.True(Directory.Exists(target),
          $"[Windows] удаление junction удалило и ЦЕЛЬ (каталог \"{target}\" исчез) — уборка фикстуры " +
          "так теряет данные вне root");
      Assert.Equal(markerBytes, File.ReadAllBytes(marker));
    }
    finally
    {
      TryDeleteLink(link);
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Группа 3: семантика записи сквозь ссылку (риск, который D3c обязан отклонять)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// <para>Измеряет, КУДА физически попадает запись по пути назначения, если один из компонентов —
  /// заранее созданная ссылка на внешний каталог. Модель атаки профиля B: в каталоге назначения
  /// (<c>root</c>) лежит <c>link → sentinel</c>, архив просит запись <c>root/link/file.txt</c>.</para>
  /// <para>Утверждается факт поведения ОС: файл побайтово появляется В SENTINEL (вне root), а внутри
  /// root не появляется ничего, кроме самой ссылки. Значит лексическая проверка имени и проверка
  /// «есть ли файл по пути» не закрывают профиль B — D3c обязан отклонять reparse-компоненты цепочки.
  /// Это характеризация механизма, а не доказательство дефекта production-кода (реальный маршрут
  /// извлекает RED-тест D3b).</para>
  /// </summary>
  [Fact]
  public void Probe_ЗаписьСквозьSymbolicLink_ФайлПоявляетсяВSentinelВнеRoot()
  {
    string root = NewRoot("wt-sym");
    string sentinel = NewRoot("wt-sym-sentinel");
    string link = Path.Combine(root, "link");
    string throughLink = Path.Combine(link, "file.txt");
    string escaped = Path.Combine(sentinel, "file.txt");
    byte[] payload = [10, 20, 30, 40, 50];

    try
    {
      string? creation = TryCreateDirectorySymlink(link, sentinel);
      if (creation is not null)
      {
        Assert.Skip($"[{OsName()}] ссылка на внешний sentinel не создана ({creation}) — сценарий " +
                    "записи сквозь ссылку в этом окружении не воспроизводится");
      }

      File.WriteAllBytes(throughLink, payload);

      Assert.True(File.Exists(escaped),
          $"[{OsName()}] запись по пути root/link/file.txt НЕ создала файл в sentinel (\"{escaped}\"): " +
          "ожидание группы 3 не подтвердилось — формулировка риска для D3c требует пересмотра");

      // Побайтовая сверка: содержимое уехало именно в sentinel.
      Assert.Equal(payload, File.ReadAllBytes(escaped));

      // Тот же объект читается и «сквозь» ссылку: это один и тот же файл, а не копия в root.
      Assert.Equal(payload, File.ReadAllBytes(throughLink));

      // Внутри root не появилось ничего, кроме самой ссылки (в sentinel ведёт запись, а не дубликат).
      string[] entries = Directory.GetFileSystemEntries(root);
      Assert.Single(entries);
      Assert.Equal(link, entries[0]);
      Assert.False(File.Exists(Path.Combine(root, "file.txt")),
          $"[{OsName()}] наряду с файлом в sentinel появился файл непосредственно в root " +
          $"(\"{Path.Combine(root, "file.txt")}\") — запись не уходила бы вне root");

      // Ссылка по-прежнему обнаруживается по общему флагу (запись её не «съела»).
      Assert.True(HasReparsePoint(link),
          $"[{OsName()}] после записи сквозь ссылку флаг ReparsePoint исчез " +
          $"(измерено: {File.GetAttributes(link)}) — после мутации обнаружение цепочки ненадёжно");
    }
    finally
    {
      // Порядок важен: сначала файл внутри sentinel, затем САМА ссылка, затем sentinel, затем root.
      TryDeleteFile(escaped);
      TryDeleteLink(link);
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
    }
  }

  /// <summary>
  /// <para>То же измерение для junction (<c>root/link → sentinel</c>), созданного внешним процессом:
  /// запись по пути назначения сквозь junction уходит ВНЕ root. Нужно для формулировки риска D3c на
  /// Windows, где junction — основной вектор (обычные привилегии не требуются).</para>
  /// <para>Вне Windows и при отказе mklink — видимый <c>Assert.Skip</c> с измеренной причиной.</para>
  /// </summary>
  [Fact]
  public void Probe_ЗаписьСквозьJunction_ФайлПоявляетсяВSentinelВнеRoot()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip($"[{OsName()}] junction — объект Windows: сценарий записи сквозь junction здесь " +
                  "не воспроизводится (симметричный замер для symbolic link есть в " +
                  "Probe_ЗаписьСквозьSymbolicLink_ФайлПоявляетсяВSentinelВнеRoot)");
    }

    string root = NewRoot("wt-jun");
    string sentinel = NewRoot("wt-jun-sentinel");
    string link = Path.Combine(root, "link");
    string throughLink = Path.Combine(link, "file.txt");
    string escaped = Path.Combine(sentinel, "file.txt");
    byte[] payload = [11, 22, 33, 44, 55];

    try
    {
      string? creation = TryCreateJunction(link, sentinel);
      if (creation is not null)
      {
        Assert.Skip($"[Windows] junction на внешний sentinel не создан ({creation}) — сценарий " +
                    "записи сквозь junction в этом окружении не воспроизводится");
      }

      File.WriteAllBytes(throughLink, payload);

      Assert.True(File.Exists(escaped),
          $"[Windows] запись по пути root/link/file.txt сквозь junction НЕ создала файл в sentinel " +
          $"(\"{escaped}\") — ожидание группы 3 не подтвердилось");

      Assert.Equal(payload, File.ReadAllBytes(escaped));
      Assert.Equal(payload, File.ReadAllBytes(throughLink));

      string[] entries = Directory.GetFileSystemEntries(root);
      Assert.Single(entries);
      Assert.Equal(link, entries[0]);

      Assert.True(HasReparsePoint(link),
          $"[Windows] после записи сквозь junction флаг ReparsePoint исчез " +
          $"(измерено: {File.GetAttributes(link)})");
    }
    finally
    {
      TryDeleteFile(escaped);
      TryDeleteLink(link);
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
    }
  }
}
