using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Тесты pre-flight проверки свободного места перед распаковкой ZIP (SEC-001, шаг B3b-1).
/// Все тесты детерминированные: диск не заполняется, требуются только заведомо крайние значения.
/// <para>
/// Честная граница: эти тесты проверяют контракт решения и отсутствие записи при нехватке места,
/// но НЕ доказывают порядок «проверка строго до первой мутации» — созданный каталог удалился бы
/// откатом в обоих случаях. Порядок обеспечен местом вставки в <c>ExtractCore</c> (после валидации
/// путей, до <c>Directory.CreateDirectory(root)</c>) и подтверждён ревью.
/// </para>
/// </summary>
public sealed class ZipExtractPreFlightSpaceTests
{
  /// <summary>Создаёт уникальную временную папку (не создавая её на диске).</summary>
  private static string NewTempDir()
    => Path.Combine(Path.GetTempPath(), "lzs-preflight-" + Guid.NewGuid().ToString("N"));

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
  /// Ветка <c>sizeAt == null</c> (обратная совместимость internal API): проверка места
  /// пропускается целиком, извлечение выполняется.
  /// </summary>
  [Fact]
  public void ExtractCore_РазмерыНеПереданы_ПроверкаМестаПропускается()
  {
    ZipEntry[] entries = [new("file.txt", Payload("Hello, preflight!"), IsDirectory: false)];
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

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(1, calls[0]);
      Assert.Equal("Hello, preflight!", File.ReadAllText(Path.Combine(dest, "file.txt")));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Контракт решения: при заведомо завышенном требовании (<c>long.MaxValue / 2</c>) на реальном
  /// томе <see cref="ZipExtractor.HasInsufficientSpace"/> возвращает <c>true</c>.
  /// Тест зависит от доступности измерения тома; если измерение недоступно, проверка места
  /// пропускается и этот тест падает — что и является сигналом, а не ложной тревогой.
  /// </summary>
  [Fact]
  public void HasInsufficientSpace_ТребуетсяБольшеЧемСвободно_ВозвращаетTrue()
  {
    string dest = Path.GetFullPath(NewTempDir());
    Assert.True(ZipExtractor.HasInsufficientSpace(dest, long.MaxValue / 2));
  }

  /// <summary>
  /// При нулевом требовании <see cref="ZipExtractor.HasInsufficientSpace"/> возвращает <c>false</c>:
  /// на любом готовом томе свободного места не меньше нуля.
  /// </summary>
  [Fact]
  public void HasInsufficientSpace_ТребованиеНоль_ВозвращаетFalse()
  {
    string dest = Path.GetFullPath(NewTempDir());
    Assert.False(ZipExtractor.HasInsufficientSpace(dest, 0));
  }

  /// <summary>
  /// Главный тест шага: при заведомо невозможном объёме <c>ExtractCore</c> возвращает
  /// <see cref="ZipExtractResult.InsufficientSpace"/>, делегат записи не вызывается ни разу
  /// и результатов извлечения не остаётся.
  /// </summary>
  [Fact]
  public void ExtractCore_НехваткаМеста_ВозвращаетInsufficientSpace_ИНеПишетДанные()
  {
    ZipEntry[] entries = [new("file.txt", Payload("Hello, preflight!"), IsDirectory: false)];
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
          sizeAt: _ => long.MaxValue / 2);

      Assert.Equal(ZipExtractResult.InsufficientSpace, result);
      Assert.Equal(0, calls[0]);
      Assert.False(Directory.Exists(dest), "результатов извлечения остаться не должно");
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Защита от переполнения: два элемента по <c>long.MaxValue / 2</c> дают сумму больше
  /// <see cref="long.MaxValue"/>. Без насыщения сумма переполнилась бы в отрицательную,
  /// и проверка места молча перестала бы срабатывать.
  /// </summary>
  [Fact]
  public void ExtractCore_ПереполнениеСуммыРазмеров_ПроверкаМестаСрабатывает()
  {
    ZipEntry[] entries =
    [
      new("a.bin", Payload("a"), IsDirectory: false),
      new("b.bin", Payload("b"), IsDirectory: false),
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
          sizeAt: _ => long.MaxValue / 2);

      Assert.Equal(ZipExtractResult.InsufficientSpace, result);
      Assert.Equal(0, calls[0]);
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Отрицательный размер из недоверенного заголовка (Zip64-значение ≥ 2^63 после приведения
  /// к <see cref="long"/>) отбрасывается и не приводит к ложному отказу: извлечение выполняется.
  /// </summary>
  [Fact]
  public void ExtractCore_ОтрицательныйРазмерИзЗаголовка_НеБлокируетИзвлечение()
  {
    ZipEntry[] entries = [new("file.txt", Payload("negative size"), IsDirectory: false)];
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
          sizeAt: _ => -1);

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(1, calls[0]);
      Assert.Equal("negative size", File.ReadAllText(Path.Combine(dest, "file.txt")));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Разрешение тома: возвращается непустой корень, который является префиксом пути назначения.
  /// Тест выполняется на всех трёх ОС CI и фиксирует фактическое поведение
  /// <c>DriveInfo.GetDrives()</c> (на POSIX ожидаются точки монтирования), поэтому выбор тома
  /// не остаётся недоказанным допущением.
  /// </summary>
  [Fact]
  public void TryResolveVolumeRoot_ДляПутиНазначения_ВозвращаетПрефиксЭтогоПути()
  {
    string dest = Path.GetFullPath(NewTempDir());

    Assert.True(ZipExtractor.TryResolveVolumeRoot(dest, out string volumeRoot),
        $"том не определён для пути {dest}");
    Assert.False(string.IsNullOrEmpty(volumeRoot));

    string normalized = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    StringComparison cmp = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    // Пустая строка после нормализации — корень "/" на POSIX: любой абсолютный путь под ним.
    Assert.True(normalized.Length == 0 || dest.StartsWith(normalized, cmp),
        $"разрешённый том '{volumeRoot}' не является префиксом пути '{dest}'");
  }

  /// <summary>
  /// Защита правила «только в конец enum»: существующие значения не изменились, новое значение
  /// <see cref="ZipExtractResult.InsufficientSpace"/> == 4.
  /// </summary>
  [Fact]
  public void ZipExtractResult_НовоеЗначение_АддитивноИВКонце()
  {
    Assert.Equal(0, (int)ZipExtractResult.Ok);
    Assert.Equal(1, (int)ZipExtractResult.InvalidData);
    Assert.Equal(2, (int)ZipExtractResult.IOError);
    Assert.Equal(3, (int)ZipExtractResult.WrongPassword);
    Assert.Equal(4, (int)ZipExtractResult.InsufficientSpace);
  }
}
