using Xunit;

namespace Lzma.Core.Tests;

/// <summary>
/// Регрессии на находки кросс-семейного ревью шага B3 (журнал staged-извлечения).
/// <para>
/// BLOCKING 2: откат обязан быть идемпотентным по последствию — повторный вызов
/// <see cref="StagedExtractionJournal.Rollback"/> не должен удалять уже восстановленные прежние байты.
/// До исправления второй вызов удалял восстановленный файл, потому что запись журнала оставалась
/// помеченной опубликованной, а backup к тому моменту уже был перенесён на место.
/// </para>
/// <para>
/// Дополнительно фиксируется one-shot семантика: после <see cref="StagedExtractionJournal.Commit"/>
/// откат не выполняется (иначе успешная операция теряла бы опубликованные данные).
/// </para>
/// Тип <see cref="StagedExtractionJournal"/> является internal и доступен тестам через
/// <c>InternalsVisibleTo</c> в <c>src/Lzma.Core/Lzma.Core.csproj</c>.
/// </summary>
public sealed class StagedExtractionJournalRollbackTests
{
  /// <summary>Уникальный временный каталог для изоляции теста.</summary>
  private static string TempDir()
      => Path.Combine(Path.GetTempPath(), "lzs-journal-" + Guid.NewGuid().ToString("N"));

  /// <summary>
  /// Публикация заменяет прежний файл (через backup), первый откат возвращает прежние байты,
  /// а повторный откат не меняет состояние: файл остаётся на месте с прежним содержимым,
  /// остатков <c>.bak</c>/<c>.tmp</c> нет. С шага B6 (по находке ревью) дополнительно на Windows
  /// проверяется сохранность атрибутов прежнего файла (<c>ReadOnly|Hidden</c>) через
  /// публикацию→откат: rename-back возвращает тот же файловый объект.
  /// </summary>
  [Fact]
  public void Откат_ПовторныйВызов_НеУдаляетВосстановленныеПрежниеБайты()
  {
    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);

      string final = Path.Combine(dest, "file.bin");
      byte[] original = [1, 2, 3, 4, 5];
      byte[] replacement = [9, 9, 9];
      File.WriteAllBytes(final, original);

      // Атрибуты — только на Windows (на POSIX Hidden нестандартен; capability-probe — todo #39).
      const FileAttributes Protected = FileAttributes.ReadOnly | FileAttributes.Hidden;
      if (OperatingSystem.IsWindows())
        File.SetAttributes(final, Protected);

      var journal = new StagedExtractionJournal();
      string staged = journal.CreateStagedPath(final);
      File.WriteAllBytes(staged, replacement);

      // Публикация: прежний файл уезжает в .bak, staged занимает его место.
      journal.Publish(staged, final);
      Assert.Equal(replacement, File.ReadAllBytes(final));

      // Первый откат: прежние байты возвращаются на своё имя.
      journal.Rollback();
      Assert.True(File.Exists(final), "после отката прежний файл отсутствует");
      Assert.Equal(original, File.ReadAllBytes(final));
      if (OperatingSystem.IsWindows())
        Assert.Equal(Protected, File.GetAttributes(final) & Protected);

      // Второй откат не должен ничего сломать (идемпотентность по последствию).
      journal.Rollback();
      Assert.True(File.Exists(final), "повторный откат удалил восстановленный файл");
      Assert.Equal(original, File.ReadAllBytes(final));
      if (OperatingSystem.IsWindows())
        Assert.Equal(Protected, File.GetAttributes(final) & Protected);

      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Single(Directory.GetFiles(dest));
    }
    finally
    {
      // Снять атрибуты перед удалением, иначе Directory.Delete откажет (Windows).
      if (Directory.Exists(dest))
      {
        foreach (string f in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
          File.SetAttributes(f, FileAttributes.Normal);

        Directory.Delete(dest, true);
      }
    }
  }

  /// <summary>
  /// После точки фиксации откат не выполняется: опубликованные данные остаются,
  /// а backup удаляется best-effort очисткой.
  /// </summary>
  [Fact]
  public void Откат_ПослеCommit_НеВыполняется_ИДанныеОстаютсяОпубликованными()
  {
    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);

      string final = Path.Combine(dest, "file.bin");
      byte[] original = [7, 7, 7];
      byte[] replacement = [1, 2, 3, 4];
      File.WriteAllBytes(final, original);

      var journal = new StagedExtractionJournal();
      string staged = journal.CreateStagedPath(final);
      File.WriteAllBytes(staged, replacement);

      journal.Publish(staged, final);
      journal.Commit();
      journal.CleanupAfterCommit();

      // Откат после точки фиксации — no-op.
      journal.Rollback();

      Assert.Equal(replacement, File.ReadAllBytes(final));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Single(Directory.GetFiles(dest));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, true);
    }
  }

  /// <summary>
  /// Откат при отказе публикации: если публикация не выполнялась (элемент только в стадии),
  /// прежний файл остаётся нетронутым, а временный файл удаляется.
  /// </summary>
  [Fact]
  public void Откат_ДоПубликации_УдаляетСтадию_ИНеТрогаетПрежнийФайл()
  {
    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);

      string final = Path.Combine(dest, "keep.bin");
      byte[] original = [4, 5, 6];
      File.WriteAllBytes(final, original);

      var journal = new StagedExtractionJournal();
      string staged = journal.CreateStagedPath(final);
      File.WriteAllBytes(staged, [1, 1]);
      journal.TrackUnpublishedStaged(staged);

      journal.Rollback();

      Assert.Equal(original, File.ReadAllBytes(final));
      Assert.False(File.Exists(staged), "временный файл не удалён при откате");
      Assert.Single(Directory.GetFiles(dest));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, true);
    }
  }
}
