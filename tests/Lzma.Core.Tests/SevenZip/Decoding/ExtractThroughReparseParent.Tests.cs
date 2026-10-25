using System.Diagnostics;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.SevenZip.Decoding;

/// <summary>
/// <para>RED-тесты уязвимости SEC-003 профиль B (шаг D3b): извлечение сквозь reparse-компонент
/// (symbolic link / junction) в цепочке пути назначения. Архив содержит элемент с лексически
/// корректным именем <c>&lt;имя-ссылки&gt;/payload.bin</c>; в каталоге назначения заранее создана
/// ссылка на внешний sentinel-каталог. Production-код (HEAD caff8f1) не проверяет
/// <c>FileAttributes.ReparsePoint</c> ни на одном компоненте цепочки (0 вхождений <c>ReparsePoint</c>
/// в <c>src/</c>), поэтому запись проходит сквозь ссылку и файл физически появляется в sentinel
/// вне root.</para>
/// <para>Четыре теста (2 маршрута × 2 типа ссылки):</para>
/// <list type="number">
/// <item>ZIP + symbolic link</item>
/// <item>7z + symbolic link</item>
/// <item>ZIP + junction (только Windows)</item>
/// <item>7z + junction (только Windows)</item>
/// </list>
/// <para>Каждый тест: (1) создаёт sentinel с маркерным файлом, (2) создаёт ссылку
/// <c>root/link → sentinel</c>, (3) строит архив с элементом <c>link/payload.bin</c>,
/// (4) извлекает в root, (5) утверждает, что sentinel НЕ изменён (маркерный файл побайтово цел,
/// <c>payload.bin</c> в sentinel отсутствует), (6) утверждает, что результат извлечения НЕ <c>Ok</c>
/// (точный код фиксируется в D3c), (7) утверждает отсутствие мусора <c>.tmp/.bak</c> в root.</para>
/// <para>Если окружение не позволяет создать ссылку — видимый <c>Assert.Skip</c> с типом
/// исключения и причиной (НЕ тихий <c>return</c>, НЕ «passed»).</para>
/// <para>Production-код НЕ изменён. Это RED-тесты: на текущем HEAD они обязаны ПАДАТЬ,
/// демонстрируя уязвимость. После фикса D3c — зелёные.</para>
/// </summary>
public sealed class ExtractThroughReparseParentTests
{
  // ─────────────────────────────────────────────────────────────────────────────
  // Общие хелперы (переиспользованы из ReparsePointCapabilityProbe.Tests)
  // ─────────────────────────────────────────────────────────────────────────────

  private const int MkLinkTimeoutMilliseconds = 30_000;
  private const int StreamDrainTimeoutMilliseconds = 2_000;
  private const int DiagnosticTextCeiling = 400;

  private static string OsName() =>
      OperatingSystem.IsWindows() ? "Windows"
      : OperatingSystem.IsMacOS() ? "macOS"
      : OperatingSystem.IsLinux() ? "Linux"
      : "Unknown";

  /// <summary>Канонический temp-корень (покомпонентная канонизация — см. ReparsePointCapabilityProbe).</summary>
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
        result = candidate;
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

  private static string Diagnostic(string? text)
  {
    string oneLine = (text ?? string.Empty)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace('\n', ' ');
    return oneLine.Length <= DiagnosticTextCeiling
        ? oneLine
        : oneLine.Substring(0, DiagnosticTextCeiling) + "…";
  }

  /// <summary>Создаёт symbolic link на каталог. Возвращает null при успехе, причину при отказе.</summary>
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

  /// <summary>Создаёт junction внешним процессом (cmd /d /c mklink /J). Только Windows.</summary>
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
    catch (InvalidOperationException) { }
    catch (System.ComponentModel.Win32Exception) { }
  }

  /// <summary>Удаляет САМУ ссылку (не цель).</summary>
  private static string? TryDeleteLink(string linkPath)
  {
    try
    {
      if (!Directory.Exists(linkPath) && !File.Exists(linkPath))
        return null;

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
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
  }

  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    }
    catch (IOException) { }
    catch (UnauthorizedAccessException) { }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Построение архивов
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>Строит ZIP-архив с одним файлом.</summary>
  private static byte[] BuildZipArchive(string entryName, byte[] content)
  {
    ZipWriteResult wr = ZipWriter.Build(
        [new ZipWriterEntry(entryName, content)],
        out byte[] archive);
    Assert.Equal(ZipWriteResult.Ok, wr);
    return archive;
  }

  /// <summary>Строит 7z-архив (LZMA2) с одним файлом.</summary>
  private static byte[] Build7zArchive(string entryName, byte[] content)
  {
    var entries = new[] { new SevenZipArchiveWriterEntry(entryName, content) };
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));
    return archive;
  }

  // Делегат для построения архива: (entryName, content) → archive bytes.
  private delegate byte[] BuildArchiveDelegate(string entryName, byte[] content);

  // ─────────────────────────────────────────────────────────────────────────────
  // Общий сценарий атаки
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Общий сценарий: sentinel с маркером, ссылка root/link → sentinel, извлечение архива
  /// с элементом "link/payload.bin" в root. Утверждает, что sentinel НЕ изменён.
  /// Делегат <paramref name="extract"/> возвращает true, если результат извлечения Ok.
  /// </summary>
  private static void RunExtractionAttack(
      string root,
      string sentinel,
      string link,
      byte[] markerBytes,
      byte[] payloadBytes,
      string linkName,
      string linkType,
      BuildArchiveDelegate buildArchive,
      Func<byte[], string, bool> extract)
  {
    string markerInSentinel = Path.Combine(sentinel, "marker.bin");
    string payloadInSentinel = Path.Combine(sentinel, "payload.bin");

    // Извлечение: production пишет сквозь ссылку → payload.bin появляется в sentinel.
    byte[] archive = buildArchive($"{linkName}/payload.bin", payloadBytes);

    try
    {
      bool isOk = extract(archive, root);

      // ГЛАВНОЕ УТВЕРЖДЕНИЕ (красноречивее кода результата): sentinel НЕ должен быть изменён.
      // Маркерный файл обязан остаться побайтово целым.
      Assert.True(File.Exists(markerInSentinel),
          $"[{OsName()}] {linkType}: маркерный файл в sentinel исчез — это не атака, а повреждение фикстуры");
      Assert.Equal(markerBytes, File.ReadAllBytes(markerInSentinel));

      // payload.bin НЕ должен появиться в sentinel (это и есть уязвимость).
      // На текущем HEAD (caff8f1) это утверждение ПАДАЕТ: production пишет сквозь ссылку.
      Assert.False(File.Exists(payloadInSentinel),
          $"[{OsName()}] {linkType}: payload.bin появился в sentinel (\"{payloadInSentinel}\") — " +
          "извлечение прошло сквозь ссылку, файл записан вне root. " +
          "Это и есть уязвимость SEC-003 профиль B, которую D3c обязан закрыть.");

      // Перечисляем sentinel рекурсивно: ожидаем только marker.bin, ничего больше.
      string[] sentinelEntries = Directory.GetFileSystemEntries(sentinel, "*", SearchOption.AllDirectories);
      Assert.Single(sentinelEntries);
      Assert.Equal(markerInSentinel, sentinelEntries[0]);

      // В root не осталось мусора .tmp/.bak.
      string[] rootEntries = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories);
      foreach (string entry in rootEntries)
      {
        string ext = Path.GetExtension(entry);
        Assert.NotEqual(".tmp", ext);
        Assert.NotEqual(".bak", ext);
      }

      // После фикса D3c результат извлечения обязан быть НЕ Ok.
      // Точный код фиксируется в D3c; чтобы тест не пришлось переписывать, используем NotEqual.
      Assert.False(isOk,
          $"[{OsName()}] {linkType}: результат извлечения Ok, но payload.bin не попал в sentinel — " +
          "неожиданное состояние: либо атака не воспроизведена, либо фикс уже применён частично");
    }
    finally
    {
      // Порядок: payload из sentinel → САМА ссылка → sentinel → root.
      TryDeleteFile(payloadInSentinel);
      TryDeleteLink(link);
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Тест 1: ZIP + symbolic link
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// RED-тест: ZIP-маршрут извлечения сквозь symbolic link. Архив содержит элемент
  /// <c>link/payload.bin</c>; в root заранее создана ссылка <c>link → sentinel</c>.
  /// На текущем HEAD (caff8f1) тест ПАДАЕТ: payload.bin появляется в sentinel вне root,
  /// что доказывает уязвимость SEC-003 профиль B.
  /// </summary>
  [Fact]
  public void Zip_SymbolicLink_SentinelНеИзменён()
  {
    string root = NewRoot("reparse-zip-sym");
    string sentinel = NewRoot("reparse-zip-sym-sentinel");
    string link = Path.Combine(root, "link");
    byte[] markerBytes = [0xAA, 0xBB, 0xCC, 0xDD];
    byte[] payloadBytes = Encoding.UTF8.GetBytes("payload through symlink");

    // Создаём sentinel с маркером.
    string markerInSentinel = Path.Combine(sentinel, "marker.bin");
    File.WriteAllBytes(markerInSentinel, markerBytes);

    // Создаём symbolic link: root/link → sentinel.
    string? creation = TryCreateDirectorySymlink(link, sentinel);
    if (creation is not null)
    {
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
      Assert.Skip($"[{OsName()}] окружение не позволило создать symbolic link через " +
                  $"Directory.CreateSymbolicLink: {creation} — на Windows это означает отсутствие " +
                  "привилегии SeCreateSymbolicLink при невключённом Developer Mode. " +
                  "RED-тест не воспроизводится в этом окружении");
    }

    RunExtractionAttack(
        root, sentinel, link, markerBytes, payloadBytes,
        linkName: "link",
        linkType: "symbolic link",
        buildArchive: BuildZipArchive,
        extract: (archive, dest) =>
        {
          // Распаковываем архив в память, чтобы получить ZipEntry[].
          Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] entries));
          ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);
          return result == ZipExtractResult.Ok;
        });
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Тест 2: 7z + symbolic link
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// RED-тест: 7z-маршрут извлечения сквозь symbolic link. Архив (LZMA2) содержит элемент
  /// <c>link/payload.bin</c>; в root заранее создана ссылка <c>link → sentinel</c>.
  /// На текущем HEAD (caff8f1) тест ПАДАЕТ: payload.bin появляется в sentinel вне root.
  /// </summary>
  [Fact]
  public void SevenZip_SymbolicLink_SentinelНеИзменён()
  {
    string root = NewRoot("reparse-7z-sym");
    string sentinel = NewRoot("reparse-7z-sym-sentinel");
    string link = Path.Combine(root, "link");
    byte[] markerBytes = [0x11, 0x22, 0x33, 0x44];
    byte[] payloadBytes = Encoding.UTF8.GetBytes("payload through symlink 7z");

    string markerInSentinel = Path.Combine(sentinel, "marker.bin");
    File.WriteAllBytes(markerInSentinel, markerBytes);

    string? creation = TryCreateDirectorySymlink(link, sentinel);
    if (creation is not null)
    {
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
      Assert.Skip($"[{OsName()}] окружение не позволило создать symbolic link через " +
                  $"Directory.CreateSymbolicLink: {creation} — на Windows это означает отсутствие " +
                  "привилегии SeCreateSymbolicLink при невключённом Developer Mode. " +
                  "RED-тест не воспроизводится в этом окружении");
    }

    RunExtractionAttack(
        root, sentinel, link, markerBytes, payloadBytes,
        linkName: "link",
        linkType: "symbolic link",
        buildArchive: Build7zArchive,
        extract: (archive, dest) =>
        {
          SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
              archive: archive,
              options: SevenZipDecodeOptions.Default,
              destinationDirectory: dest,
              overwrite: false,
              bytesConsumed: out _);
          return result == SevenZipArchiveDecodeResult.Ok;
        });
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Тест 3: ZIP + junction (только Windows)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// RED-тест: ZIP-маршрут извлечения сквозь junction (только Windows). Архив содержит элемент
  /// <c>link/payload.bin</c>; в root заранее создана junction <c>link → sentinel</c> внешним
  /// процессом (<c>cmd /d /c mklink /J</c>). Вне Windows тест пропускается с причиной
  /// «junction — объект Windows».
  /// На текущем HEAD (caff8f1) тест ПАДАЕТ: payload.bin появляется в sentinel вне root.
  /// </summary>
  [Fact]
  public void Zip_Junction_SentinelНеИзменён()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip($"[{OsName()}] junction — объект Windows (NTFS reparse point, создаётся встроенной " +
                  "командой cmd mklink /J); managed API его не создаёт, P/Invoke запрещён владельцем " +
                  "→ сценарий на этой ОС не воспроизводится");
    }

    string root = NewRoot("reparse-zip-jun");
    string sentinel = NewRoot("reparse-zip-jun-sentinel");
    string link = Path.Combine(root, "link");
    byte[] markerBytes = [0x55, 0x66, 0x77, 0x88];
    byte[] payloadBytes = Encoding.UTF8.GetBytes("payload through junction");

    string markerInSentinel = Path.Combine(sentinel, "marker.bin");
    File.WriteAllBytes(markerInSentinel, markerBytes);

    string? creation = TryCreateJunction(link, sentinel);
    if (creation is not null)
    {
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
      Assert.Skip($"[Windows] внешний процесс не создал junction: {creation} — права/политики хоста " +
                  "(mklink /J обычно не требует привилегий). RED-тест не воспроизводится");
    }

    RunExtractionAttack(
        root, sentinel, link, markerBytes, payloadBytes,
        linkName: "link",
        linkType: "junction",
        buildArchive: BuildZipArchive,
        extract: (archive, dest) =>
        {
          Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] entries));
          ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);
          return result == ZipExtractResult.Ok;
        });
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // Тест 4: 7z + junction (только Windows)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// RED-тест: 7z-маршрут извлечения сквозь junction (только Windows). Архив (LZMA2) содержит
  /// элемент <c>link/payload.bin</c>; в root заранее создана junction <c>link → sentinel</c>
  /// внешним процессом. Вне Windows тест пропускается с причиной «junction — объект Windows».
  /// На текущем HEAD (caff8f1) тест ПАДАЕТ: payload.bin появляется в sentinel вне root.
  /// </summary>
  [Fact]
  public void SevenZip_Junction_SentinelНеИзменён()
  {
    if (!OperatingSystem.IsWindows())
    {
      Assert.Skip($"[{OsName()}] junction — объект Windows (NTFS reparse point, создаётся встроенной " +
                  "командой cmd mklink /J); managed API его не создаёт, P/Invoke запрещён владельцем " +
                  "→ сценарий на этой ОС не воспроизводится");
    }

    string root = NewRoot("reparse-7z-jun");
    string sentinel = NewRoot("reparse-7z-jun-sentinel");
    string link = Path.Combine(root, "link");
    byte[] markerBytes = [0x99, 0xAA, 0xBB, 0xCC];
    byte[] payloadBytes = Encoding.UTF8.GetBytes("payload through junction 7z");

    string markerInSentinel = Path.Combine(sentinel, "marker.bin");
    File.WriteAllBytes(markerInSentinel, markerBytes);

    string? creation = TryCreateJunction(link, sentinel);
    if (creation is not null)
    {
      TryDeleteTree(sentinel);
      TryDeleteTree(root);
      Assert.Skip($"[Windows] внешний процесс не создал junction: {creation} — права/политики хоста " +
                  "(mklink /J обычно не требует привилегий). RED-тест не воспроизводится");
    }

    RunExtractionAttack(
        root, sentinel, link, markerBytes, payloadBytes,
        linkName: "link",
        linkType: "junction",
        buildArchive: Build7zArchive,
        extract: (archive, dest) =>
        {
          SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
              archive: archive,
              options: SevenZipDecodeOptions.Default,
              destinationDirectory: dest,
              overwrite: false,
              bytesConsumed: out _);
          return result == SevenZipArchiveDecodeResult.Ok;
        });
  }
}
