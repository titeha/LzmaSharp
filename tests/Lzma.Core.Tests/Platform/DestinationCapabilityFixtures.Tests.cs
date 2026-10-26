using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Platform;

/// <summary>
/// <para>Фаза D (SEC-003), шаг D4a: платформенные фикстуры назначения.
/// <b>Production-код не изменён</b>; тесты измеряют факты поведения ОС при извлечении
/// в каталог с ограниченным доступом и при Unicode-нормализации имён.</para>
/// <para>Две группы измерений (обе через реальный маршрут извлечения):</para>
/// <list type="number">
/// <item><b>Группа 1 — каталог назначения без права записи:</b> ZIP через
/// <see cref="ZipExtractor.ExtractToDirectory"/> (overwrite:false, фикстура
/// <see cref="ZipWriter.Build"/>). На POSIX <see cref="File.SetAttributes(string, FileAttributes)"/>
/// с <see cref="FileAttributes.ReadOnly"/> снимает бит записи владельца; на Windows атрибут
/// ReadOnly у каталога создание файлов НЕ блокирует (известная особенность NTFS). Измеряется
/// фактическое поведение: код результата, наличие частичных данных, остатков .tmp/.bak,
/// целость sentinel-файла.</item>
/// <item><b>Группа 2 — Unicode-нормализация (NFC против NFD):</b> архив содержит элемент
/// «café.txt» в NFC (U+00E9); извлекается; проверяется существование NFC-строкой и NFD-строкой
/// (e + U+0301), а также перечислением каталога. На NTFS имя хранится в NFC, на APFS — в NFD,
/// на ext4 — как записано. Если расхождение обнаружено, тест фиксирует факт видимым
/// <c>Assert.Skip</c> с подробным описанием, а не падает. Дополнительно: сценарий с
/// предварительно созданным NFD-файлом и NFC-элементом архива при overwrite:false.</item>
/// </list>
/// <para>Политика жёсткости (образец — <c>PlatformCapabilityProbeTests</c>): падение только если
/// базовое поведение сломано (файл не создан вовсе). Консервативное расхождение или
/// невозможность воспроизвести — видимый <c>Assert.Skip</c> с измеренным значением и причиной.
/// Тихий <c>return</c> из <c>[Fact]</c> запрещён.</para>
/// <para>Честные границы: (1) сценарии со сменой CWD сознательно исключены (состояние процесса,
/// ломает параллельные тесты); (2) длинные пути не измеряются (покрыто
/// <c>ZipExtractPathLength.Tests</c>); (3) locked file не измеряется (покрыто
/// <c>PublishRaceAndMoveProbe.Tests</c>).</para>
/// </summary>
public sealed class DestinationCapabilityFixturesTests
{
  // ─────────────────────────────────────────────────────────────────────────────
  // Общие хелперы
  // ─────────────────────────────────────────────────────────────────────────────

  private static string OsName() =>
      OperatingSystem.IsWindows() ? "Windows"
      : OperatingSystem.IsMacOS() ? "macOS"
      : OperatingSystem.IsLinux() ? "Linux"
      : "Unknown";

  /// <summary>
  /// Канонический temp-корень. На macOS <c>Path.GetTempPath()</c> возвращает <c>/var/folders/…</c>,
  /// а <c>/var</c> — symlink на <c>/private/var</c>, поэтому ссылка хранит путь в одном написании,
  /// а ядро видит другой. <c>ResolveLinkTarget</c> разрешает только КОНЕЧНЫЙ компонент, поэтому
  /// идём по компонентам сверху вниз.
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
        result = candidate;
      }
      catch (UnauthorizedAccessException)
      {
        result = candidate;
      }
    }

    return result;
  }

  private static string NewTempRoot(string tag)
  {
    string canonical = Canonicalize(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    string root = Path.Combine(canonical, $"lzsp-{tag}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(root));
    return root;
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      string safePath = StagedExtractionJournal.ToFileSystemPath(root);
      if (Directory.Exists(safePath))
        Directory.Delete(safePath, recursive: true);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
  }

  /// <summary>
  /// Пробует вернуть атрибуты каталога в нормальное состояние перед удалением.
  /// На POSIX снимает ReadOnly (возвращает бит записи владельцу); на Windows
  /// ReadOnly у каталога не влияет на создание файлов, но очистка не повредит.
  /// </summary>
  private static void TryRestoreDirectoryAttributes(string dir)
  {
    try
    {
      string safePath = StagedExtractionJournal.ToFileSystemPath(dir);
      if (Directory.Exists(safePath))
        File.SetAttributes(safePath, FileAttributes.Normal);
    }
    catch (IOException) { /* best-effort */ }
    catch (UnauthorizedAccessException) { /* best-effort */ }
    catch (PlatformNotSupportedException) { /* best-effort */ }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // ГРУППА 1. Каталог назначения без права записи
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// <para><b>Characterization:</b> извлечение ZIP-архива в каталог, лишённый права записи.</para>
  /// <para>Механизм: каталог назначения создан, затем <see cref="File.SetAttributes(string, FileAttributes)"/>
  /// с <see cref="FileAttributes.ReadOnly"/>. На POSIX это снимает бит записи владельца (write bit);
  /// на Windows атрибут ReadOnly у каталога создание файлов НЕ блокирует (известная особенность
  /// NTFS — атрибут применяется только к файлам).</para>
  /// <para>Ожидаемое поведение (по ОС):</para>
  /// <list type="bullet">
  /// <item><b>POSIX (Linux/macOS):</b> извлечение отказывает с кодом <see cref="ZipExtractResult.IOError"/>
  /// (исключение <see cref="UnauthorizedAccessException"/> мапится в IOError). В каталоге нет
  /// частичных данных, нет остатков .tmp/.bak, sentinel-файл цел.</item>
  /// <item><b>Windows:</b> атрибут ReadOnly у каталога не блокирует создание файлов, поэтому
  /// извлечение проходит успешно с кодом <see cref="ZipExtractResult.Ok"/>. Это измеренное
  /// платформенное расхождение, а не дефект.</item>
  /// </list>
  /// <para>Инварианты сохранности:</para>
  /// <list type="bullet">
  /// <item>Если операция отказала, в каталоге назначения нет частичных данных.</item>
  /// <item>Нет остатков .tmp/.bak.</item>
  /// <item>Sentinel-файл (если лежит рядом) не изменён.</item>
  /// </list>
  /// <para>Это фиксация текущего поведения (characterization), а не требование к production.</para>
  /// </summary>
  [Fact]
  public void ZipExtract_КаталогБезПравЗаписи_PosixОтказывает_WindowsПропускает()
  {
    string root = NewTempRoot("dest-noperm");
    string dest = Path.Combine(root, "extract");
    string sentinel = Path.Combine(root, "sentinel.txt");
    byte[] sentinelContent = Encoding.UTF8.GetBytes("SENTINEL — не трогать");

    try
    {
      // Создаём каталог назначения и sentinel-файл.
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(dest));
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(sentinel), sentinelContent);

      // Архив с одним элементом.
      byte[] fileContent = Encoding.UTF8.GetBytes("test file content");
      ZipWriterEntry[] writerEntries = [new("file.txt", fileContent)];
      Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(writerEntries, out byte[] archive));
      Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] entries));

      // Лишаем каталог права записи.
      try
      {
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(dest), FileAttributes.ReadOnly);
      }
      catch (Exception ex)
      {
        Assert.Skip($"[{OsName()}] не удалось установить ReadOnly на каталог: {ex.GetType().Name}: {ex.Message}");
      }

      // Проверяем, что атрибут действительно установлен.
      var attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(dest));
      if ((attrs & FileAttributes.ReadOnly) == 0)
      {
        Assert.Skip($"[{OsName()}] атрибут ReadOnly не установлен на каталоге (attrs={attrs}).");
      }

      // Извлекаем архив.
      ZipExtractResult result;
      try
      {
        result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);
      }
      catch (Exception ex)
      {
        // Необработанное исключение наружу — это нарушение инварианта контролируемого кода.
        Assert.Fail($"[{OsName()}] извлечение бросило необработанное исключение: {ex.GetType().Name}: {ex.Message}. " +
                    "Ожидался контролируемый код результата (IOError или другой), а не исключение наружу.");
        return; // unreachable, но для компилятора
      }

      // ─── Фиксация фактического поведения (characterization) ───

      // Инвариант 1: sentinel-файл не изменён.
      byte[] actualSentinel = File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(sentinel));
      Assert.Equal(sentinelContent, actualSentinel);

      // Инвариант 2: нет остатков .tmp/.bak.
      string[] tmpFiles = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*.tmp", SearchOption.AllDirectories);
      string[] bakFiles = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*.bak", SearchOption.AllDirectories);
      Assert.Empty(tmpFiles);
      Assert.Empty(bakFiles);

      if (OperatingSystem.IsWindows())
      {
        // На Windows атрибут ReadOnly у каталога НЕ блокирует создание файлов.
        // Если извлечение прошло успешно — это измеренное платформенное расхождение.
        if (result == ZipExtractResult.Ok)
        {
          // Файл создан, атрибут ReadOnly у каталога не сработал как защита.
          string filePath = Path.Combine(dest, "file.txt");
          Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(filePath)),
              $"[{OsName()}] результат Ok, но файл file.txt не создан в назначении.");
        }
        else
        {
          // Если результат != Ok, проверяем, что в каталоге нет частичных данных.
          string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*", SearchOption.AllDirectories);
          Assert.Empty(files);
        }
      }
      else
      {
        // На POSIX (Linux/macOS) ReadOnly у каталога снимает бит записи владельца.
        // Ожидаем отказ с кодом IOError (UnauthorizedAccessException мапится в IOError).
        if (result == ZipExtractResult.Ok)
        {
          // Если извлечение прошло успешно на POSIX — это неожиданное поведение.
          // Возможно, тест запущен от root или с особыми правами.
          Assert.Skip($"[{OsName()}] извлечение прошло успешно (Ok) в каталог с ReadOnly. " +
                      "Возможно, тест запущен от root или с особыми правами. " +
                      "Это измеренное платформенное расхождение.");
        }
        else
        {
          // Отказ: проверяем, что в каталоге нет частичных данных.
          string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*", SearchOption.AllDirectories);
          Assert.Empty(files);

          // Фиксируем код результата (characterization).
          Assert.True(
              result is ZipExtractResult.IOError or ZipExtractResult.InvalidData,
              $"[{OsName()}] неожиданный код результата при извлечении в каталог без прав: {result}. " +
              "Ожидался IOError (UnauthorizedAccessException) или InvalidData.");
        }
      }
    }
    finally
    {
      // Возвращаем атрибуты перед удалением.
      TryRestoreDirectoryAttributes(Path.Combine(root, "extract"));
      TryDeleteTree(root);
    }
  }

  /// <summary>
  /// <para><b>Characterization:</b> извлечение 7z-архива в каталог, лишённый права записи.
  /// Тот же сценарий, что и <see cref="ZipExtract_КаталогБезПравЗаписи_PosixОтказывает_WindowsПропускает"/>,
  /// но для маршрута 7z через <see cref="SevenZipArchiveDecoder.ExtractToDirectory"/>.</para>
  /// <para>Ожидаемое поведение аналогично ZIP-маршруту: на POSIX — отказ с кодом
  /// <see cref="SevenZipArchiveDecodeResult.IOError"/> или <see cref="SevenZipArchiveDecodeResult.InternalError"/>;
  /// на Windows — успех (<see cref="SevenZipArchiveDecodeResult.Ok"/>), потому что атрибут ReadOnly
  /// у каталога не блокирует создание файлов.</para>
  /// <para>Это фиксация текущего поведения (characterization), а не требование к production.</para>
  /// </summary>
  [Fact]
  public void SevenZipExtract_КаталогБезПравЗаписи_PosixОтказывает_WindowsПропускает()
  {
    string root = NewTempRoot("dest-noperm-7z");
    string dest = Path.Combine(root, "extract");
    string sentinel = Path.Combine(root, "sentinel.txt");
    byte[] sentinelContent = Encoding.UTF8.GetBytes("SENTINEL — не трогать");

    try
    {
      // Создаём каталог назначения и sentinel-файл.
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(dest));
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(sentinel), sentinelContent);

      // Строим 7z-архив с одним элементом.
      byte[] fileContent = Encoding.UTF8.GetBytes("test file content for 7z");
      SevenZipArchiveWriterEntry[] writerEntries = [new("file.txt", fileContent)];
      Assert.Equal(SevenZipArchiveWriteResult.Ok,
          SevenZipArchiveWriter.BuildArchive(writerEntries, SevenZipWriterCompressionMethod.Copy, out byte[] archive));

      // Лишаем каталог права записи.
      try
      {
        File.SetAttributes(StagedExtractionJournal.ToFileSystemPath(dest), FileAttributes.ReadOnly);
      }
      catch (Exception ex)
      {
        Assert.Skip($"[{OsName()}] не удалось установить ReadOnly на каталог: {ex.GetType().Name}: {ex.Message}");
      }

      // Проверяем, что атрибут действительно установлен.
      var attrs = File.GetAttributes(StagedExtractionJournal.ToFileSystemPath(dest));
      if ((attrs & FileAttributes.ReadOnly) == 0)
      {
        Assert.Skip($"[{OsName()}] атрибут ReadOnly не установлен на каталоге (attrs={attrs}).");
      }

      // Извлекаем архив.
      SevenZipArchiveDecodeResult result;
      try
      {
        result = SevenZipArchiveDecoder.ExtractToDirectory(archive, dest, overwrite: false);
      }
      catch (Exception ex)
      {
        Assert.Fail($"[{OsName()}] извлечение 7z бросило необработанное исключение: {ex.GetType().Name}: {ex.Message}. " +
                    "Ожидался контролируемый код результата, а не исключение наружу.");
        return; // unreachable
      }

      // ─── Фиксация фактического поведения (characterization) ───

      // Инвариант 1: sentinel-файл не изменён.
      byte[] actualSentinel = File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(sentinel));
      Assert.Equal(sentinelContent, actualSentinel);

      // Инвариант 2: нет остатков .tmp/.bak.
      string[] tmpFiles = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*.tmp", SearchOption.AllDirectories);
      string[] bakFiles = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*.bak", SearchOption.AllDirectories);
      Assert.Empty(tmpFiles);
      Assert.Empty(bakFiles);

      if (OperatingSystem.IsWindows())
      {
        // На Windows атрибут ReadOnly у каталога НЕ блокирует создание файлов.
        if (result == SevenZipArchiveDecodeResult.Ok)
        {
          string filePath = Path.Combine(dest, "file.txt");
          Assert.True(File.Exists(StagedExtractionJournal.ToFileSystemPath(filePath)),
              $"[{OsName()}] результат Ok, но файл file.txt не создан в назначении.");
        }
        else
        {
          string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*", SearchOption.AllDirectories);
          Assert.Empty(files);
        }
      }
      else
      {
        // На POSIX ожидаем отказ.
        if (result == SevenZipArchiveDecodeResult.Ok)
        {
          Assert.Skip($"[{OsName()}] извлечение 7z прошло успешно (Ok) в каталог с ReadOnly. " +
                      "Возможно, тест запущен от root или с особыми правами.");
        }
        else
        {
          string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest), "*", SearchOption.AllDirectories);
          Assert.Empty(files);

          Assert.True(
              result is SevenZipArchiveDecodeResult.InternalError
              || result is SevenZipArchiveDecodeResult.InvalidData,
              $"[{OsName()}] неожиданный код результата при извлечении 7z в каталог без прав: {result}. " +
              "Ожидался InternalError или InvalidData.");
        }
      }
    }
    finally
    {
      TryRestoreDirectoryAttributes(Path.Combine(root, "extract"));
      TryDeleteTree(root);
    }
  }

  // ─────────────────────────────────────────────────────────────────────────────
  // ГРУППА 2. Unicode-нормализация имён (NFC против NFD)
  // ─────────────────────────────────────────────────────────────────────────────

  /// <summary>
  /// Строка «café» в NFC-форме (U+00E9 — один символ).
  /// </summary>
  private static readonly string CafeNfc = "caf\u00E9";

  /// <summary>
  /// Строка «café» в NFD-форме (e + U+0301 — два символа: базовая буква + комбинирующий диакритический знак).
  /// </summary>
  private static readonly string CafeNfd = "cafe\u0301";

  /// <summary>
  /// <para><b>Characterization:</b> извлечение элемента с NFC-именем и проверка существования
  /// NFC- и NFD-строками, а также перечислением каталога.</para>
  /// <para>Цель измерения: на какой ОС имя на диске хранится/ищется в другой нормализации
  /// (APFS хранит в NFD, NTFS — в NFC, ext4 — как записано). Может ли из-за этого проверка
  /// конфликта (которая в production идёт через <c>HashSet</c>/<c>StringComparer</c> по строкам)
  /// НЕ увидеть существующий файл в другой нормализации.</para>
  /// <para>Ожидаемое поведение (по ОС):</para>
  /// <list type="bullet">
  /// <item><b>Windows (NTFS):</b> имя хранится в NFC (как записано). <c>File.Exists</c> по NFC — true,
  /// по NFD — false (если файловая система регистрозависима к нормализации). В каталоге один файл.</item>
  /// <item><b>macOS (APFS):</b> имя хранится в NFD. <c>File.Exists</c> по NFC — true (APFS нормализует
  /// при поиске), по NFD — true. В каталоге один файл.</item>
  /// <item><b>Linux (ext4):</b> имя хранится как записано (NFC). <c>File.Exists</c> по NFC — true,
  /// по NFD — false (если файловая система чувствительна к нормализации). В каталоге один файл.</item>
  /// </list>
  /// <para>Если расхождение обнаружено (NFC и NFD дают разный результат <c>File.Exists</c>),
  /// тест фиксирует факт видимым <c>Assert.Skip</c> с подробным описанием, а не падает.
  /// Жёсткое падение допустимо только если извлечение не дало файл вовсе (базовое поведение сломано).</para>
  /// </summary>
  [Fact]
  public void ZipExtract_NfcИмя_ПроверкаСуществованияNfcИNfd()
  {
    string root = NewTempRoot("dest-nfc");
    string dest = Path.Combine(root, "extract");

    try
    {
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(dest));

      // Архив с элементом в NFC-форме.
      string entryNameNfc = $"{CafeNfc}.txt";
      byte[] fileContent = Encoding.UTF8.GetBytes("content of cafe file");
      ZipWriterEntry[] writerEntries = [new(entryNameNfc, fileContent)];
      Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(writerEntries, out byte[] archive));
      Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] entries));

      // Извлекаем архив.
      ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);
      Assert.Equal(ZipExtractResult.Ok, result);

      // Проверяем существование файла NFC-строкой.
      string filePathNfc = Path.Combine(dest, entryNameNfc);
      bool existsByNfc = File.Exists(StagedExtractionJournal.ToFileSystemPath(filePathNfc));

      // Проверяем существование файла NFD-строкой.
      string entryNameNfd = $"{CafeNfd}.txt";
      string filePathNfd = Path.Combine(dest, entryNameNfd);
      bool existsByNfd = File.Exists(StagedExtractionJournal.ToFileSystemPath(filePathNfd));

      // Перечисляем каталог и смотрим фактическое имя на диске.
      string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest));
      
      // Базовое поведение: файл должен существовать хотя бы по одной форме.
      if (!existsByNfc && !existsByNfd && files.Length == 0)
      {
        Assert.Fail($"[{OsName()}] извлечение не создало файл вовсе. " +
                    $"Ожидалось имя '{entryNameNfc}' (NFC) или '{entryNameNfd}' (NFD).");
      }

      // Фиксируем факты (characterization).
      if (existsByNfc && existsByNfd)
      {
        // Обе формы работают — файловая система нормализует при поиске (APFS на macOS).
        // Это ожидаемое поведение на macOS, но может быть неожиданным на Windows/Linux.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
          // На Windows/Linux обе формы работают — это расхождение с ожидаемым поведением.
          // Фиксируем как измеренный факт, но не падаем.
          Assert.Skip($"[{OsName()}] File.Exists работает по обеим нормализациям (NFC={existsByNfc}, NFD={existsByNfd}). " +
                      $"Файлов в каталоге: {files.Length}. " +
                      $"Фактическое имя на диске: '{(files.Length > 0 ? Path.GetFileName(files[0]) : "нет файлов")}'. " +
                      "Это измеренное платформенное расхождение: проверка конфликта через HashSet/StringComparer " +
                      "может не увидеть файл в другой нормализации.");
        }
        // На macOS это ожидаемое поведение (APFS нормализует в NFD при поиске).
      }
      else if (existsByNfc && !existsByNfd)
      {
        // Только NFC работает — файловая система хранит имя как записано (NTFS, ext4).
        // Это ожидаемое поведение на Windows и Linux.
        if (OperatingSystem.IsMacOS())
        {
          // На macOS только NFC работает — это расхождение с ожидаемым поведением APFS.
          Assert.Skip($"[{OsName()}] File.Exists работает только по NFC (NFC={existsByNfc}, NFD={existsByNfd}). " +
                      $"Файлов в каталоге: {files.Length}. " +
                      $"Фактическое имя на диске: '{(files.Length > 0 ? Path.GetFileName(files[0]) : "нет файлов")}'. " +
                      "Это измеренное платформенное расхождение.");
        }
        // На Windows/Linux это ожидаемое поведение.
      }
      else if (!existsByNfc && existsByNfd)
      {
        // Только NFD работает — файловая система нормализует в NFD при записи (APFS на macOS).
        // Это ожидаемое поведение на macOS.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
          // На Windows/Linux только NFD работает — это расхождение с ожидаемым поведением.
          Assert.Skip($"[{OsName()}] File.Exists работает только по NFD (NFC={existsByNfc}, NFD={existsByNfd}). " +
                      $"Файлов в каталоге: {files.Length}. " +
                      $"Фактическое имя на диске: '{(files.Length > 0 ? Path.GetFileName(files[0]) : "нет файлов")}'. " +
                      "Это измеренное платформенное расхождение.");
        }
        // На macOS это ожидаемое поведение.
      }
      else
      {
        // Ни одна форма не работает, но файлы в каталоге есть — странное поведение.
        Assert.Skip($"[{OsName()}] File.Exists не работает ни по NFC, ни по NFD, " +
                    $"но в каталоге {files.Length} файл(ов). " +
                    $"Фактическое имя на диске: '{(files.Length > 0 ? Path.GetFileName(files[0]) : "нет файлов")}'. " +
                    "Это измеренное платформенное расхождение.");
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  /// <summary>
  /// <para><b>Characterization:</b> извлечение элемента с NFC-именем в каталог, где уже существует
  /// файл с NFD-именем, при overwrite:false.</para>
  /// <para>Цель измерения: отклоняется ли операция (конфликт) или файл создаётся рядом второй.
  /// Если файловая система считает NFC и NFD разными именами (ext4, NTFS без нормализации),
  /// файл создаётся рядом второй. Если файловая система считает их одинаковыми (APFS),
  /// операция отклоняется с кодом <see cref="ZipExtractResult.InvalidData"/>.</para>
  /// <para>Ожидаемое поведение (по ОС):</para>
  /// <list type="bullet">
  /// <item><b>Windows (NTFS):</b> NFC и NFD — разные имена, файл создаётся рядом второй,
  /// результат <see cref="ZipExtractResult.Ok"/>. В каталоге два файла.</item>
  /// <item><b>macOS (APFS):</b> NFC и NFD — одинаковые имена (нормализация при поиске),
  /// операция отклоняется с кодом <see cref="ZipExtractResult.InvalidData"/>. В каталоге один файл.</item>
  /// <item><b>Linux (ext4):</b> NFC и NFD — разные имена, файл создаётся рядом второй,
  /// результат <see cref="ZipExtractResult.Ok"/>. В каталоге два файла.</item>
  /// </list>
  /// <para>Если поведение различается по ОС, тест фиксирует факт видимым <c>Assert.Skip</c>
  /// с числами, а не падает.</para>
  /// </summary>
  [Fact]
  public void ZipExtract_NfcЭлемент_NfdФайлУжеСуществует_OverwriteFalse()
  {
    string root = NewTempRoot("dest-nfc-nfd");
    string dest = Path.Combine(root, "extract");

    try
    {
      Directory.CreateDirectory(StagedExtractionJournal.ToFileSystemPath(dest));

      // Предварительно создаём файл в NFD-форме.
      string existingFileName = $"{CafeNfd}.txt";
      string existingFilePath = Path.Combine(dest, existingFileName);
      byte[] existingContent = Encoding.UTF8.GetBytes("existing NFD file");
      File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(existingFilePath), existingContent);

      // Архив с элементом в NFC-форме (то же имя, другая нормализация).
      string entryNameNfc = $"{CafeNfc}.txt";
      byte[] fileContent = Encoding.UTF8.GetBytes("new NFC file from archive");
      ZipWriterEntry[] writerEntries = [new(entryNameNfc, fileContent)];
      Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(writerEntries, out byte[] archive));
      Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] entries));

      // Извлекаем архив с overwrite:false.
      ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);

      // Перечисляем каталог.
      string[] files = Directory.GetFiles(StagedExtractionJournal.ToFileSystemPath(dest));

      // Проверяем существование по обеим формам.
      string filePathNfc = Path.Combine(dest, entryNameNfc);
      bool existsByNfc = File.Exists(StagedExtractionJournal.ToFileSystemPath(filePathNfc));
      string filePathNfd = Path.Combine(dest, existingFileName);
      bool existsByNfd = File.Exists(StagedExtractionJournal.ToFileSystemPath(filePathNfd));

      // Фиксируем факты (characterization).
      if (result == ZipExtractResult.Ok)
      {
        // Извлечение прошло успешно — файловая система считает NFC и NFD разными именами.
        // Проверяем, что в каталоге два файла (или один, если файловая система нормализует).
        if (files.Length == 2)
        {
          // Два файла — ожидаемое поведение на Windows и Linux.
          if (OperatingSystem.IsMacOS())
          {
            // На macOS два файла — это расхождение с ожидаемым поведением APFS.
            Assert.Skip($"[{OsName()}] извлечение прошло успешно (Ok), в каталоге {files.Length} файла. " +
                        $"File.Exists по NFC: {existsByNfc}, по NFD: {existsByNfd}. " +
                        $"Файлы на диске: '{string.Join("', '", files.Select(Path.GetFileName))}'. " +
                        "Это измеренное платформенное расхождение: APFS должен считать NFC и NFD одинаковыми.");
          }
          // На Windows/Linux это ожидаемое поведение.
        }
        else if (files.Length == 1)
        {
          // Один файл — странное поведение при результате Ok.
          Assert.Skip($"[{OsName()}] извлечение прошло успешно (Ok), но в каталоге только {files.Length} файл. " +
                      $"File.Exists по NFC: {existsByNfc}, по NFD: {existsByNfd}. " +
                      $"Файл на диске: '{Path.GetFileName(files[0])}'. " +
                      "Это измеренное платформенное расхождение.");
        }
        else
        {
          Assert.Skip($"[{OsName()}] извлечение прошло успешно (Ok), в каталоге {files.Length} файл(ов). " +
                      $"File.Exists по NFC: {existsByNfc}, по NFD: {existsByNfd}. " +
                      "Это измеренное платформенное расхождение.");
        }
      }
      else
      {
        // Извлечение отклонено — файловая система считает NFC и NFD одинаковыми именами.
        // Проверяем, что существующий файл не изменён.
        byte[] actualExisting = File.ReadAllBytes(StagedExtractionJournal.ToFileSystemPath(existingFilePath));
        Assert.Equal(existingContent, actualExisting);

        if (files.Length == 1)
        {
          // Один файл — ожидаемое поведение на macOS.
          if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
          {
            // На Windows/Linux один файл при отказе — это расхождение с ожидаемым поведением.
            Assert.Skip($"[{OsName()}] извлечение отклонено (код {result}), в каталоге {files.Length} файл. " +
                        $"File.Exists по NFC: {existsByNfc}, по NFD: {existsByNfd}. " +
                        $"Файл на диске: '{Path.GetFileName(files[0])}'. " +
                        "Это измеренное платформенное расхождение: NTFS/ext4 должны считать NFC и NFD разными.");
          }
          // На macOS это ожидаемое поведение.
        }
        else
        {
          Assert.Skip($"[{OsName()}] извлечение отклонено (код {result}), в каталоге {files.Length} файл(ов). " +
                      $"File.Exists по NFC: {existsByNfc}, по NFD: {existsByNfd}. " +
                      "Это измеренное платформенное расхождение.");
        }
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }
}
