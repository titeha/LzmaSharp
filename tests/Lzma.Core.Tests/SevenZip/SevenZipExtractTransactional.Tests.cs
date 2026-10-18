using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Блокирующие и характеризационные тесты SEC-001 для 7z-маршрута:
/// TX-EX-04 (прежние байты при отказе с <c>overwrite: true</c>), TX-EX-05 (много-членная
/// семантика: отказ/отмена ПОСЛЕ публикации части членов → вся операция откатывается,
/// опубликованные файлы восстанавливаются из <c>.bak</c>), TX-EX-06 (атрибуты прежнего
/// файла не мутируются до успеха) и характеризации отказных состояний (PathTooLong на
/// компоненте 240, InternalError на заблокированном назначении, проброс
/// <see cref="OperationCanceledException"/>).
/// <para>
/// До фикса B5a (коммит <c>68bbfaf</c>) тесты TX-EX-04 ПАДАЛИ по правильной причине — потеря
/// прежних байт. Механизм отката опубликованных членов реализован в B3/B5a/B6; тесты
/// TX-EX-05/отмены/блокировки — характеризации (зелёные сразу, фиксируют ветви, ранее
/// подтверждённые только чтением кода и ревью).
/// </para>
/// </summary>
public sealed class SevenZipExtractTransactionalTests
{
  // Контент архива (сверено с SevenZipReal7zAesLzma2SolidMultiFileTests.CreateExpectedFiles).
  private static readonly byte[] ArchiveAlphaBytes = Encoding.UTF8.GetBytes(
      "alpha alpha alpha alpha alpha\r\n"
    + "solid AES LZMA2 test payload A\r\n");

  private static readonly byte[] ArchiveBetaBytes = Encoding.UTF8.GetBytes(
      "beta beta beta beta beta\r\n"
    + "solid AES LZMA2 test payload B\r\n");

  // Sentinel-байты: детерминированные, заведомо отличаются от содержимого архива.
  // Размеры — небольшие (десятки байт), но не пустые (для empty.bin именно это вскрывало
  // перезапись пустым массивом до фикса B5a).
  private static readonly byte[] SentinelA = CreateSentinel(seed: 101, length: 48);
  private static readonly byte[] SentinelB = CreateSentinel(seed: 202, length: 48);
  private static readonly byte[] SentinelC = CreateSentinel(seed: 303, length: 48);

  /// <summary>
  /// TX-EX-04, полный набор членов: при извлечении solid-folder архива с неверным паролем
  /// и <c>overwrite: true</c> прежние байты всех трёх членов (<c>alpha.txt</c>, <c>nested/beta.txt</c>,
  /// <c>empty.bin</c>) обязаны быть сохранены.
  /// <para>
  /// До фикса B5a падал: <c>empty.bin</c> перезаписывался пустым массивом, data-файлы
  /// обрезались и удалялись откатом. Теперь блокирует регресс стадинга 7z-маршрута.
  /// </para>
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_НеверныйПароль_SolidFolder_ПрежниеБайтыВсехЧленовСохранены()
  {
    // 1. Чтение фикстуры.
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_solid_multifile_pwd_mhe_off.7z");
    Assert.NotEmpty(archive);

    // 2. Setup: sentinel-файлы для всех трёх членов.
    string dest = CreateTempRoot();
    try
    {
      string alphaPath = Path.Combine(dest, "alpha.txt");
      string betaPath = Path.Combine(dest, "nested", "beta.txt");
      string emptyPath = Path.Combine(dest, "empty.bin");

      Directory.CreateDirectory(Path.Combine(dest, "nested"));

      File.WriteAllBytes(alphaPath, SentinelA);
      File.WriteAllBytes(betaPath, SentinelB);
      File.WriteAllBytes(emptyPath, SentinelC);

      // Утверждаем, что sentinel-байты отличаются от содержимого архива.
      Assert.NotEqual(ArchiveAlphaBytes, SentinelA);
      Assert.NotEqual(ArchiveBetaBytes, SentinelB);
      // empty.bin в архиве — пустой; sentinel НЕ пустой ⇒ заведомо отличаются.
      Assert.NotEmpty(SentinelC);

      // 3. Извлечение с неверным паролем и overwrite: true.
      using SevenZipPassword wrong = SevenZipPassword.FromString("wrong-password");
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrong),
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _);

      // 4. Утверждения (ДО cleanup).
      Assert.NotEqual(SevenZipArchiveDecodeResult.Ok, result);

      // Каждый из трёх sentinel-файлов существует и сохраняет прежние байты.
      Assert.True(File.Exists(alphaPath), "прежний файл удалён — потеря данных (SEC-001): alpha.txt");
      Assert.Equal(SentinelA, File.ReadAllBytes(alphaPath));

      Assert.True(File.Exists(betaPath), "прежний файл удалён — потеря данных (SEC-001): nested/beta.txt");
      Assert.Equal(SentinelB, File.ReadAllBytes(betaPath));

      Assert.True(File.Exists(emptyPath), "прежний файл удалён — потеря данных (SEC-001): empty.bin");
      Assert.Equal(SentinelC, File.ReadAllBytes(emptyPath));

      // Остатков *.tmp и *.bak нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));

      // Посторонних файлов в назначении нет: только три ожидаемых имени.
      string[] allFiles = Directory.GetFiles(dest, "*", SearchOption.AllDirectories);
      Assert.Equal(3, allFiles.Length);
    }
    finally
    {
      TryDeleteTree(dest);
    }
  }

  /// <summary>
  /// TX-EX-04, частичное извлечение: при <c>shouldExtract: name => name == "alpha.txt"</c>
  /// и неверном пароле прежние байты <c>alpha.txt</c> обязаны быть сохранены.
  /// Невыбранные подпотоки идут в <c>Stream.Null</c>, но folder декодируется целиком,
  /// поэтому отказ при неверном пароле всё равно обязан откатить выбранный член.
  /// <para>
  /// До фикса B5a падал: <c>alpha.txt</c> обрезался и удалялся откатом.
  /// </para>
  /// </summary>
  [Fact]
  public void Извлечь_Частичное_НеверныйПароль_ПрежниеБайтыВыбранногоЧленаСохранены()
  {
    // 1. Чтение фикстуры.
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_solid_multifile_pwd_mhe_off.7z");
    Assert.NotEmpty(archive);

    // 2. Setup: sentinel только для alpha.txt.
    string dest = CreateTempRoot();
    try
    {
      Directory.CreateDirectory(dest);
      string alphaPath = Path.Combine(dest, "alpha.txt");
      File.WriteAllBytes(alphaPath, SentinelA);

      Assert.NotEqual(ArchiveAlphaBytes, SentinelA);

      // 3. Частичное извлечение с неверным паролем и overwrite: true.
      using SevenZipPassword wrong = SevenZipPassword.FromString("wrong-password");
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrong),
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _,
          shouldExtract: name => name == "alpha.txt");

      // 4. Утверждения (ДО cleanup).
      Assert.NotEqual(SevenZipArchiveDecodeResult.Ok, result);

      Assert.True(File.Exists(alphaPath), "прежний файл удалён — потеря данных (SEC-001): alpha.txt");
      Assert.Equal(SentinelA, File.ReadAllBytes(alphaPath));

      // Остатков нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTree(dest);
    }
  }

  private static byte[] CreateSentinel(int seed, int length)
  {
    var rng = new Random(seed);
    var buf = new byte[length];
    rng.NextBytes(buf);
    return buf;
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipExtractTransactionalTests),
        Guid.NewGuid().ToString("N"));
  }

  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    }
    catch
    {
      // best-effort cleanup для тестового каталога
    }
  }

  /// <summary>
  /// Удаляет дерево каталогов, предварительно снимая ReadOnly/Hidden с файлов
  /// (иначе <c>Directory.Delete</c> на Windows откажет по ReadOnly-файлам).
  /// </summary>
  private static void TryDeleteTreeWithAttributes(string path)
  {
    try
    {
      if (!Directory.Exists(path))
        return;

      foreach (string filePath in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
        File.SetAttributes(filePath, FileAttributes.Normal);

      Directory.Delete(path, recursive: true);
    }
    catch
    {
      // best-effort cleanup
    }
  }

  /// <summary>
  /// TX-EX-06: при извлечении с <c>overwrite: true</c> и неверным паролем прежние байты
  /// И атрибуты (ReadOnly | Hidden) всех трёх членов обязаны быть сохранены.
  /// <para>
  /// До фикса B6 вызов <c>TryPrepareExistingFileForOverwrite</c> сбрасывал атрибуты
  /// существующего файла назначения <c>File.SetAttributes(fullPath, FileAttributes.Normal)</c>
  /// до проверки пароля; при отказе откат возвращал байты, но атрибуты были утрачены.
  /// </para>
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_НеверныйПароль_АтрибутыПрежнегоФайлаСохранены()
  {
    // 1. Чтение фикстуры (та же, что TX-EX-04).
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_solid_multifile_pwd_mhe_off.7z");
    Assert.NotEmpty(archive);

    // 2. Setup: sentinel-файлы с атрибутами ReadOnly | Hidden.
    string dest = CreateTempRoot();
    try
    {
      string alphaPath = Path.Combine(dest, "alpha.txt");
      string betaPath = Path.Combine(dest, "nested", "beta.txt");
      string emptyPath = Path.Combine(dest, "empty.bin");

      Directory.CreateDirectory(Path.Combine(dest, "nested"));

      File.WriteAllBytes(alphaPath, SentinelA);
      File.WriteAllBytes(betaPath, SentinelB);
      File.WriteAllBytes(emptyPath, SentinelC);

      // Атрибуты устанавливаются только на Windows: на POSIX FileAttributes.Hidden не является
      // стандартным атрибутом, а поведение SetAttributes зависит от ФС (capability-probe —
      // todo #39). Байтовая часть теста проверяется на всех ОС (решение по сомнению ревью B6).
      if (OperatingSystem.IsWindows())
      {
        File.SetAttributes(alphaPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(betaPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(emptyPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
      }

      // Утверждаем, что sentinel-байты отличаются от содержимого архива.
      Assert.NotEqual(ArchiveAlphaBytes, SentinelA);
      Assert.NotEqual(ArchiveBetaBytes, SentinelB);
      Assert.NotEmpty(SentinelC);

      // 3. Извлечение с неверным паролем и overwrite: true.
      using SevenZipPassword wrong = SevenZipPassword.FromString("wrong-password");
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrong),
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _);

      // 4. Утверждения (ДО cleanup).
      Assert.NotEqual(SevenZipArchiveDecodeResult.Ok, result);

      // Байты всех трёх sentinel-файлов сохранены (все ОС).
      Assert.True(File.Exists(alphaPath), "прежний файл удалён — потеря данных (SEC-001): alpha.txt");
      Assert.Equal(SentinelA, File.ReadAllBytes(alphaPath));

      Assert.True(File.Exists(betaPath), "прежний файл удалён — потеря данных (SEC-001): nested/beta.txt");
      Assert.Equal(SentinelB, File.ReadAllBytes(betaPath));

      Assert.True(File.Exists(emptyPath), "прежний файл удалён — потеря данных (SEC-001): empty.bin");
      Assert.Equal(SentinelC, File.ReadAllBytes(emptyPath));

      // Атрибуты ReadOnly | Hidden сохранены (Windows: атрибуты надёжны;
      // POSIX: FileAttributes.Hidden маппится платформой и может не сохраниться,
      // поэтому проверяем только на Windows).
      if (OperatingSystem.IsWindows())
      {
        Assert.True(
            (File.GetAttributes(alphaPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(alphaPath)}");

        Assert.True(
            (File.GetAttributes(betaPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(betaPath)}");

        Assert.True(
            (File.GetAttributes(emptyPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(emptyPath)}");
      }

      // Остатков *.tmp и *.bak нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>
  /// Характеризация (рекомендация ревью B5b): overwrite:true + компонент имени 240 символов
  /// + прежние байты сохранены. Архив строится штатным <c>SevenZipArchiveWriter</c>;
  /// sentinel по длинному пути создаётся через <c>StagedExtractionJournal.ToFileSystemPath</c>.
  /// Извлечение с overwrite:true возвращает <c>PathTooLong</c>, байты sentinel целы,
  /// остатков .tmp/.bak нет.
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_КомпонентИмени240_ПрежниеБайтыСохранены_PathTooLong()
  {
    // 1. Сборка архива с членом, имя которого 240 символов (236 'x' + ".txt").
    string name240 = new string('x', 236) + ".txt";
    Assert.Equal(240, name240.Length);
    byte[] archive = BuildLzma2((name240, Encoding.UTF8.GetBytes("archive-content")));

    // 2. Setup: sentinel по тому же длинному пути (через расширенную форму).
    string dest = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipExtractTransactionalTests),
        "longpath-" + Guid.NewGuid().ToString("N"));
    try
    {
      Directory.CreateDirectory(dest);
      string targetPath = Path.Combine(dest, name240);
      string extendedPath = StagedExtractionJournal.ToFileSystemPath(targetPath);

      byte[] sentinel = Encoding.UTF8.GetBytes("sentinel-long-path-content");
      File.WriteAllBytes(extendedPath, sentinel);

      // 3. Извлечение с overwrite: true.
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _);

      // 4. Утверждения.
      Assert.Equal(SevenZipArchiveDecodeResult.PathTooLong, result);

      // Байты sentinel целы.
      Assert.True(File.Exists(extendedPath), "sentinel-файл удалён — потеря данных");
      Assert.Equal(sentinel, File.ReadAllBytes(extendedPath));

      // Остатков .tmp и .bak нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>
  /// TX-EX-05: много-folder архив (Copy — каждый файл в отдельном folder), отказ ВТОРОГО
  /// folder-а после успешной публикации первого. Первый sentinel восстановлен из .bak
  /// (ветвь Published Replaced → Rollback), второй не тронут, остатков нет.
  /// <para>
  /// Copy-writer даёт один folder на файл (подтверждено <c>TryWriteCopyFoldersUnpackInfo</c>:
  /// <c>sizes.Length</c> folder-ов). Packed data — сырые байты контента подряд, поэтому
  /// порча второго folder-а — подмена байта в области второго файла в packed-потоке.
  /// </para>
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_MultiFolder_ОтказВторого_ПервыйВосстановлен()
  {
    // 1. Сборка архива: 2 файла по ~2 КиБ, различимое содержимое.
    byte[] firstContent = CreateSentinel(seed: 11, length: 2048);
    byte[] secondContent = CreateSentinel(seed: 22, length: 2048);
    Assert.NotEqual(firstContent, secondContent);

    byte[] archive = BuildCopy(
        ("first.bin", firstContent),
        ("second.bin", secondContent));

    // 2. Порча: находим сырой фрагмент второго члена в архиве и искажаем один байт.
    // Copy-архив: packed data = [sig 32][first raw][second raw][next header].
    // Ищем secondContent как подстроку и инвертируем один байт.
    int corruptIndex = FindSubsequence(archive, secondContent);
    Assert.True(corruptIndex >= 0, "second content not found in archive (Copy expected raw placement)");
    archive[corruptIndex] ^= 0xFF;

    // 3. Setup: sentinels на обоих конечных путях (отличаются от содержимого архива).
    string dest = CreateTempRoot();
    try
    {
      string firstPath = Path.Combine(dest, "first.bin");
      string secondPath = Path.Combine(dest, "second.bin");

      Directory.CreateDirectory(dest);

      byte[] sentinelFirst = CreateSentinel(seed: 101, length: 64);
      byte[] sentinelSecond = CreateSentinel(seed: 202, length: 64);

      File.WriteAllBytes(firstPath, sentinelFirst);
      File.WriteAllBytes(secondPath, sentinelSecond);

      if (OperatingSystem.IsWindows())
      {
        File.SetAttributes(firstPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(secondPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
      }

      // 4. Извлечение с overwrite: true — ожидаем отказ (CRC второго folder-а не сойдётся).
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _);

      // 5. Утверждения. Код фиксируем точно: InvalidData = CRC-mismatch ВТОРОГО folder'а
      // (ревью B7, m-2); порча расположена после данных первого члена, значит первый folder
      // был раскодирован и ОПУБЛИКОВАН до отказа — наблюдаем именно целевую ветвь отката.
      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.True(corruptIndex >= 32 + firstContent.Length,
          "порча должна находиться во втором pack-потоке (после 32-байтного sig-заголовка и данных первого члена)");

      // Первый sentinel восстановлен (bytes).
      Assert.True(File.Exists(firstPath), "первый файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinelFirst, File.ReadAllBytes(firstPath));

      // Второй sentinel не тронут.
      Assert.True(File.Exists(secondPath), "второй файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinelSecond, File.ReadAllBytes(secondPath));

      // Атрибуты первого sentinel восстановлены (Windows).
      if (OperatingSystem.IsWindows())
      {
        Assert.True(
            (File.GetAttributes(firstPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты первого утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(firstPath)}");

        Assert.True(
            (File.GetAttributes(secondPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты второго утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(secondPath)}");
      }

      // Остатков .tmp и .bak нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>
  /// Отмена при перезаписи: заранее отменённый <see cref="CancellationToken"/> + overwrite:true
  /// + sentinel → операция не выполняется. <c>token.ThrowIfCancellationRequested()</c> бросает
  /// <see cref="OperationCanceledException"/>; внешний catch decoder-а НЕ перехватывает его
  /// (только IOException/UnauthorizedAccessException/ArgumentException/NotSupportedException),
  /// поэтому исключение пробрасывается наружу.
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_ОтменённыйToken_OperationCanceledException_SentinelЦел()
  {
    // 1. Сборка простого Copy-архива.
    byte[] content = CreateSentinel(seed: 33, length: 1024);
    byte[] archive = BuildCopy(("data.bin", content));

    // 2. Setup: sentinel.
    string dest = CreateTempRoot();
    try
    {
      string dataPath = Path.Combine(dest, "data.bin");
      Directory.CreateDirectory(dest);

      byte[] sentinel = CreateSentinel(seed: 303, length: 64);
      File.WriteAllBytes(dataPath, sentinel);

      if (OperatingSystem.IsWindows())
        File.SetAttributes(dataPath, FileAttributes.ReadOnly | FileAttributes.Hidden);

      // 3. Pre-cancelled token.
      using var cts = new CancellationTokenSource();
      cts.Cancel();

      // 4. OperationCanceledException пробрасывается наружу (не маппится в код результата).
      Assert.Throws<OperationCanceledException>(() =>
          SevenZipArchiveDecoder.ExtractToDirectory(
              archive: archive,
              options: SevenZipDecodeOptions.Default,
              destinationDirectory: dest,
              overwrite: true,
              bytesConsumed: out _,
              token: cts.Token));

      // 5. Sentinel цел (bytes).
      Assert.True(File.Exists(dataPath), "sentinel-файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinel, File.ReadAllBytes(dataPath));

      // Атрибуты сохранены (Windows).
      if (OperatingSystem.IsWindows())
      {
        Assert.True(
            (File.GetAttributes(dataPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(dataPath)}");
      }

      // Остатков нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>
  /// Заблокированное назначение (Windows-only): конечный файл открыт с <c>FileShare.None</c>,
  /// overwrite:true → публикация (rename final→.bak или staged→final) отказывает → InternalError.
  /// На POSIX — <see cref="Assert.Skip"/> (нет FileShare.None-семантики).
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_ЗаблокированноеНазначение_InternalError_SentinelЦел()
  {
    if (!OperatingSystem.IsWindows())
    {
      // POSIX: FileShare.None не блокирует переименование/открытие однозначно.
      Assert.Skip("FileShare.None locking is Windows-specific");
      return;
    }

    // 1. Сборка простого Copy-архива.
    byte[] content = CreateSentinel(seed: 44, length: 1024);
    byte[] archive = BuildCopy(("locked.bin", content));

    // 2. Setup: sentinel + блокирующий handle.
    string dest = CreateTempRoot();
    try
    {
      string lockedPath = Path.Combine(dest, "locked.bin");
      Directory.CreateDirectory(dest);

      byte[] sentinel = CreateSentinel(seed: 404, length: 64);
      File.WriteAllBytes(lockedPath, sentinel);

      // Блокируем файл: FileShare.None → любой другой open/move откажет.
      using var blocker = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

      // 3. Извлечение с overwrite: true.
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _);

      // 4. Утверждения. Код фиксируем точно: IOException из Publish (rename заблокированного
      // файла) маппится в InternalError (ревью B7, m-2).
      Assert.Equal(SevenZipArchiveDecodeResult.InternalError, result);

      // Закрываем handle перед проверкой sentinel-байтов (иначе File.ReadAllBytes откажет).
      blocker.Close();

      Assert.True(File.Exists(lockedPath), "sentinel-файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinel, File.ReadAllBytes(lockedPath));

      // Остатков нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>
  /// Отмена ПОСЛЕ публикации первого folder'а (ревью B7, M-1): Copy-архив из двух членов;
  /// отмена инициируется первым отчётом прогресса с ненулевым <c>BytesProcessed</c> — отчёт
  /// происходит до публикации текущего folder'а, публикация проходит без проверки токена,
  /// а второй folder прерывается на <c>token.ThrowIfCancellationRequested()</c>. Итог:
  /// <see cref="OperationCanceledException"/> пробрасывается, ОПУБЛИКОВАННЫЙ первый член
  /// восстанавливается из <c>.bak</c> (байты и, на Windows, атрибуты), второй не тронут,
  /// остатков нет.
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_ОтменаПослеПубликацииПервогоFolder_ПрежниеБайтыВосстановлены()
  {
    byte[] firstContent = CreateSentinel(seed: 33, length: 2048);
    byte[] secondContent = CreateSentinel(seed: 44, length: 2048);
    byte[] archive = BuildCopy(
        ("first.bin", firstContent),
        ("second.bin", secondContent));

    string dest = CreateTempRoot();
    try
    {
      string firstPath = Path.Combine(dest, "first.bin");
      string secondPath = Path.Combine(dest, "second.bin");
      Directory.CreateDirectory(dest);

      byte[] sentinelFirst = CreateSentinel(seed: 303, length: 64);
      byte[] sentinelSecond = CreateSentinel(seed: 404, length: 64);
      File.WriteAllBytes(firstPath, sentinelFirst);
      File.WriteAllBytes(secondPath, sentinelSecond);

      if (OperatingSystem.IsWindows())
      {
        File.SetAttributes(firstPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(secondPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
      }

      using var cts = new CancellationTokenSource();
      var progress = new CallbackProgress(p =>
      {
        // Первый ненулевой отчёт = декодирование первого folder'а завершено (до его публикации).
        if (p.BytesProcessed > 0)
          cts.Cancel();
      });

      Assert.Throws<OperationCanceledException>(() =>
          SevenZipArchiveDecoder.ExtractToDirectory(
              archive: archive,
              options: SevenZipDecodeOptions.Default,
              destinationDirectory: dest,
              overwrite: true,
              bytesConsumed: out _,
              progress: progress,
              token: cts.Token));

      // Опубликованный первый член восстановлен из .bak; второй не тронут.
      Assert.Equal(sentinelFirst, File.ReadAllBytes(firstPath));
      Assert.Equal(sentinelSecond, File.ReadAllBytes(secondPath));
      if (OperatingSystem.IsWindows())
      {
        const FileAttributes Protected = FileAttributes.ReadOnly | FileAttributes.Hidden;
        Assert.Equal(Protected, File.GetAttributes(firstPath) & Protected);
        Assert.Equal(Protected, File.GetAttributes(secondPath) & Protected);
      }

      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  /// <summary>Синхронный <see cref="IProgress{T}"/> без контекста синхронизации (тест отмены).</summary>
  private sealed class CallbackProgress(Action<SevenZipProgress> callback) : IProgress<SevenZipProgress>
  {
    public void Report(SevenZipProgress value) => callback(value);
  }

  /// <summary>
  /// Частичное извлечение + атрибуты (долг ревью B6): <c>shouldExtract</c> выбирает только
  /// один член из solid-folder архива с неверным паролем; выбранный sentinel восстановлен
  /// (байты + атрибуты Windows), невыбранные не тронуты (их sentinel-файлы созданы самим
  /// тестом и обязаны остаться без изменений).
  /// </summary>
  [Fact]
  public void Извлечь_Частичное_НеверныйПароль_АтрибутыВыбранногоСохранены()
  {
    // 1. Чтение фикстуры (та же, что TX-EX-04/TX-EX-06).
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_solid_multifile_pwd_mhe_off.7z");
    Assert.NotEmpty(archive);

    // 2. Setup: sentinel-файлы для всех трёх членов с атрибутами.
    string dest = CreateTempRoot();
    try
    {
      string alphaPath = Path.Combine(dest, "alpha.txt");
      string betaPath = Path.Combine(dest, "nested", "beta.txt");
      string emptyPath = Path.Combine(dest, "empty.bin");

      Directory.CreateDirectory(Path.Combine(dest, "nested"));

      File.WriteAllBytes(alphaPath, SentinelA);
      File.WriteAllBytes(betaPath, SentinelB);
      File.WriteAllBytes(emptyPath, SentinelC);

      if (OperatingSystem.IsWindows())
      {
        File.SetAttributes(alphaPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(betaPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
        File.SetAttributes(emptyPath, FileAttributes.ReadOnly | FileAttributes.Hidden);
      }

      // 3. Частичное извлечение: выбираем ТОЛЬКО alpha.txt.
      using SevenZipPassword wrong = SevenZipPassword.FromString("wrong-password");
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(wrong),
          destinationDirectory: dest,
          overwrite: true,
          bytesConsumed: out _,
          shouldExtract: name => name == "alpha.txt");

      // 4. Утверждения.
      Assert.NotEqual(SevenZipArchiveDecodeResult.Ok, result);

      // Выбранный sentinel восстановлен (байты).
      Assert.True(File.Exists(alphaPath), "выбранный файл удалён — потеря данных (SEC-001): alpha.txt");
      Assert.Equal(SentinelA, File.ReadAllBytes(alphaPath));

      // Невыбранные не тронуты.
      Assert.True(File.Exists(betaPath), "невыбранный файл удалён: nested/beta.txt");
      Assert.Equal(SentinelB, File.ReadAllBytes(betaPath));
      Assert.True(File.Exists(emptyPath), "невыбранный файл удалён: empty.bin");
      Assert.Equal(SentinelC, File.ReadAllBytes(emptyPath));

      // Атрибуты выбранного сохранены (Windows).
      if (OperatingSystem.IsWindows())
      {
        Assert.True(
            (File.GetAttributes(alphaPath) & (FileAttributes.ReadOnly | FileAttributes.Hidden))
            == (FileAttributes.ReadOnly | FileAttributes.Hidden),
            $"атрибуты выбранного утрачены: expected ReadOnly|Hidden, actual {File.GetAttributes(alphaPath)}");
      }

      // Остатков нет.
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      TryDeleteTreeWithAttributes(dest);
    }
  }

  private static byte[] BuildCopy(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Copy, out byte[] bytes));
    return bytes;
  }

  /// <summary>
  /// Ищет первое вхождение подстроки байтов в массиве. Возвращает индекс начала или -1.
  /// </summary>
  private static int FindSubsequence(byte[] haystack, byte[] needle)
  {
    if (needle.Length == 0) return 0;
    if (needle.Length > haystack.Length) return -1;

    int limit = haystack.Length - needle.Length;
    for (int i = 0; i <= limit; i++)
    {
      bool found = true;
      for (int j = 0; j < needle.Length; j++)
      {
        if (haystack[i + j] != needle[j])
        {
          found = false;
          break;
        }
      }
      if (found) return i;
    }
    return -1;
  }

  private static byte[] BuildLzma2(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] bytes));
    return bytes;
  }
}
