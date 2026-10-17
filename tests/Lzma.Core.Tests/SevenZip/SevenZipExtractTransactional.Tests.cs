using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// RED-тесты TX-EX-04 для SEC-001 (7z-маршрут): при извлечении с <c>overwrite: true</c>
/// отказ операции обязан оставить прежние байты всех затронутых файлов.
/// <para>
/// На текущем коде оба теста ПАДАЮТ по правильной причине — потеря прежних байт:
/// <list type="bullet">
/// <item><c>:1098</c> — пустой файл (<c>empty.bin</c>) перезаписывается <c>File.WriteAllBytes(fullPath, [])</c>
///   до всякой проверки пароля/CRC;</item>
/// <item><c>:1146</c> — data-файлы folder-а обрезаются через <c>FileStream(FileMode.Create)</c>
///   до проверки <c>routing.IsComplete || SizeOverflow || CrcMismatch</c> (<c>:1178</c>);</item>
/// <item><c>:1195–1211</c> — откат удаляет их как «созданные», не восстанавливая прежние байты.</item>
/// </list>
/// После реализации SEC-001 (стадинг folder-а + публикация только после проверки) оба теста
/// обязаны пройти.
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
  // Размеры — небольшие (десятки байт), но не пустые (для empty.bin именно это вскрывает
  // перезапись пустым массивом на :1098).
  private static readonly byte[] SentinelA = CreateSentinel(seed: 101, length: 48);
  private static readonly byte[] SentinelB = CreateSentinel(seed: 202, length: 48);
  private static readonly byte[] SentinelC = CreateSentinel(seed: 303, length: 48);

  /// <summary>
  /// TX-EX-04, полный набор членов: при извлечении solid-folder архива с неверным паролем
  /// и <c>overwrite: true</c> прежние байты всех трёх членов (<c>alpha.txt</c>, <c>nested/beta.txt</c>,
  /// <c>empty.bin</c>) обязаны быть сохранены.
  /// <para>
  /// На текущем коде падает: <c>empty.bin</c> перезаписан пустым массивом (<c>:1098</c>),
  /// data-файлы обрезаны (<c>:1146</c>) и удалены откатом (<c>:1195–1211</c>).
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
  /// Невыбранные подпотоки идут в <c>Stream.Null</c> (<c>:1153</c>), но folder декодируется
  /// целиком, и отказ на <c>:1178</c> + откат <c>:1195–1211</c> всё равно уничтожает файл.
  /// <para>
  /// На текущем коде падает: <c>alpha.txt</c> обрезан (<c>:1146</c>) и удалён откатом.
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
}
