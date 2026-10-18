using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Блокирующие тесты TX-EX-04 для SEC-001 (7z-маршрут): при извлечении с <c>overwrite: true</c>
/// отказ операции обязан оставить прежние байты всех затронутых файлов.
/// <para>
/// До фикса B5a (коммит <c>68bbfaf</c>) оба теста ПАДАЛИ по правильной причине — потеря прежних
/// байт: пустой файл (<c>empty.bin</c>) перезаписывался <c>File.WriteAllBytes(fullPath, [])</c>
/// до всякой проверки пароля/CRC; data-файлы folder-а обрезались через
/// <c>FileStream(FileMode.Create)</c> до проверки раскладки; откат удалял их как «созданные»,
/// не восстанавливая прежние байты. После перевода 7z-маршрута на стадинг
/// (<c>StagedExtractionJournal</c>) с публикацией folder-а только после закрытия потоков
/// и проверок результата оба теста зелёные и блокируют регресс.
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

  private static byte[] BuildLzma2(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] bytes));
    return bytes;
  }
}
