using System.Diagnostics;
using System.Text;

using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002 (§4.4 шаг 10): юнит-тесты <see cref="StagedVolumeSet"/> с инъекцией
/// <see cref="IStagedVolumeFileOperations"/> для детерминированных отказов файловых
/// операций в <see cref="StagedVolumeSet.Commit"/>.
/// </summary>
public sealed class StagedVolumeSetTests
{
  /// <summary>
  /// Регрессионный тест: если первый новый том уже опубликован (первый Move прошёл),
  /// а следующий Move выбрасывает IOException, исходный набор томов должен быть
  /// восстановлен. На текущей реализации восстановления нет — <c>archive.001</c>
  /// остаётся заменён новыми байтами, поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void Commit_MoveFailureAfterFirstPublishedVolume_RestoresOriginalSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Перед публикацией три существующих тома уходят в backup (Move #0–#2),
    // затем первый publish Move (#3) проходит, а второй publish Move (#4) падает.
    var fake = new StagedFileOperationsFake(failMoveIndex: 4);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      byte[] new002 = Encoding.UTF8.GetBytes("new-002");
      byte[] new003 = Encoding.UTF8.GetBytes("new-003");

      File.WriteAllBytes(staged001, new001);
      File.WriteAllBytes(staged002, new002);
      File.WriteAllBytes(staged003, new003);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      // Первый publish Move проходит, второй выбрасывает IOException.
      Assert.Throws<IOException>(() => set.Commit());

      // Безопасный инвариант: все три исходных тома должны остаться байт-в-байт
      // прежними. На текущем production-коде первый том уже заменён new-001.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Ни один конечный том не должен содержать new-* данных.
      Assert.DoesNotContain("new-", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.DoesNotContain("new-", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.DoesNotContain("new-", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Первые пять Move: backup phase (#0–#2), затем publish (#3 успех, #4 сбой).
      // Общее число вызовов не фиксируем: после реализации publish rollback после #4
      // последуют дополнительные восстановительные Move.
      Assert.True(fake.MoveCalls.Count >= 5, "Ожидались минимум 5 вызовов Move.");

      // Backup phase: источники — соответствующие конечные тома.
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(final002, fake.MoveCalls[1].Source);
      Assert.Equal(final003, fake.MoveCalls[2].Source);

      string backupA = fake.MoveCalls[0].Destination;
      string backupB = fake.MoveCalls[1].Destination;
      string backupC = fake.MoveCalls[2].Destination;

      // Backup-пути лежат в каталоге назначения и не совпадают с конечными именами.
      Assert.Equal(dir, Path.GetDirectoryName(backupA));
      Assert.Equal(dir, Path.GetDirectoryName(backupB));
      Assert.Equal(dir, Path.GetDirectoryName(backupC));

      // Имена backup уникальны и не привязаны к предсказуемому GUID-шаблону.
      Assert.NotEqual(backupA, backupB);
      Assert.NotEqual(backupA, backupC);
      Assert.NotEqual(backupB, backupC);
      Assert.NotEqual(final001, backupA);
      Assert.NotEqual(final002, backupB);
      Assert.NotEqual(final003, backupC);

      // Publish phase: #3 публикует первый новый том, #4 падает на втором.
      Assert.Equal(staged001, fake.MoveCalls[3].Source);
      Assert.Equal(final001, fake.MoveCalls[3].Destination);
      Assert.Equal(staged002, fake.MoveCalls[4].Source);
      Assert.Equal(final002, fake.MoveCalls[4].Destination);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Сбой на ПЕРВОМ backup Move: ни один том не опубликовывается, исходный набор
  /// остаётся байт-в-байт прежним.
  /// </summary>
  [Fact]
  public void Commit_BackupFailureOnFirstMove_PreservesOriginalSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Первый backup Move (индекс 0) выбрасывает IOException.
    var fake = new StagedFileOperationsFake(failMoveIndex: 0);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      // Посторонний файл, который не должен затрагиваться backup-фазой.
      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));
      File.WriteAllBytes(staged003, Encoding.UTF8.GetBytes("new-003"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      Assert.Throws<IOException>(() => set.Commit());

      // Исходный набор остаётся байт-в-байт прежним.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Публикации не было: ни один конечный том не содержит new-* данных.
      Assert.Equal("old-001", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.Equal("old-002", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.Equal("old-003", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Посторонний файл не тронут.
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));

      // Единственный Move — это неудачный backup первого тома; публикации нет.
      Assert.Single(fake.MoveCalls);
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(dir, Path.GetDirectoryName(fake.MoveCalls[0].Destination));
      Assert.NotEqual(final001, fake.MoveCalls[0].Destination);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Сбой на втором backup Move после успешного первого backup: первая резервная копия
  /// откатывается, исходный набор восстанавливается, публикация не начинается.
  /// </summary>
  [Fact]
  public void Commit_BackupFailureAfterFirstBackup_RestoresOriginalSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Второй backup Move (индекс 1) выбрасывает IOException.
    var fake = new StagedFileOperationsFake(failMoveIndex: 1);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));
      File.WriteAllBytes(staged003, Encoding.UTF8.GetBytes("new-003"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      Assert.Throws<IOException>(() => set.Commit());

      // Все три исходных тома восстановлены байт-в-байт.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Публикации не было: ни один конечный том не содержит new-* данных.
      Assert.Equal("old-001", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.Equal("old-002", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.Equal("old-003", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Посторонний файл не тронут.
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));

      // Последовательность Move:
      // [0] backup final001 → backupA (успех);
      // [1] backup final002 → backupB (сбой);
      // [2] restore backupA → final001.
      Assert.Equal(3, fake.MoveCalls.Count);

      string backupA = fake.MoveCalls[0].Destination;
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(dir, Path.GetDirectoryName(backupA));
      Assert.NotEqual(final001, backupA);

      Assert.Equal(final002, fake.MoveCalls[1].Source);

      Assert.Equal(backupA, fake.MoveCalls[2].Source);
      Assert.Equal(final001, fake.MoveCalls[2].Destination);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// M7 RED-тест: двойной отказ — сбой backup (#2) и сбой restore (#3).
  /// На текущем HEAD RestoreBackups не имеет per-item try/catch, поэтому после отказа #3
  /// оставшиеся backup не получают попытку восстановления. Тест падает на инвариантах
  /// best-effort: final001 не восстановлен и/или MoveCalls.Count != 5.
  /// </summary>
  [Fact]
  public void Commit_BackupFailureThenRestoreFailure_RestoresRemainingBackups()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-m7-double-fault-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Две точки отказа Move: #2 (backup final003) и #3 (restore backupB→final002).
    var failIndices = new (int Index, Exception? Failure)[]
    {
      (2, new IOException("Injected failure on Move #2 (backup final003).")),
      (3, new IOException("Injected failure on Move #3 (restore backupB→final002).")),
    };
    var fake = new StagedFileOperationsFake(failIndices);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));
      File.WriteAllBytes(staged003, Encoding.UTF8.GetBytes("new-003"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      // Инвариант 1: Commit бросает AggregateException с двумя inner.
      var ex = Assert.Throws<AggregateException>(() => set.Commit());
      Assert.True(ex.InnerExceptions.Count == 2,
          $"Инвариант 1: InnerExceptions.Count должен быть 2, фактически {ex.InnerExceptions.Count}. Журнал MoveCalls: {string.Join("; ", fake.MoveCalls.Select(m => $"{m.Source}→{m.Destination}"))}");

      // InnerExceptions[0] — backup-отказ (#2), InnerExceptions[1] — restore-отказ (#3).
      Assert.IsType<IOException>(ex.InnerExceptions[0]);
      Assert.IsType<IOException>(ex.InnerExceptions[1]);
      Assert.Contains("#2", ex.InnerExceptions[0].Message);
      Assert.Contains("#3", ex.InnerExceptions[1].Message);

      // Инвариант 5: журнал MoveCalls — на текущем HEAD Count == 4 (нет вызова #4).
      // После фикса ожидается Count == 5.
      string moveLog = string.Join("; ", fake.MoveCalls.Select((m, i) => $"#{i} {m.Source}→{m.Destination}"));
      Assert.True(fake.MoveCalls.Count == 5,
          $"Инвариант 5: MoveCalls.Count должен быть 5 (best-effort restore для backupA), фактически {fake.MoveCalls.Count}. Журнал: {moveLog}");

      // Инвариант 2: final001 восстановлен из backupA (best-effort).
      string backupA = fake.MoveCalls[0].Destination;
      Assert.True(File.Exists(final001),
          $"Инвариант 2: final001 должен существовать (восстановлен из backupA). Журнал: {moveLog}");
      Assert.Equal(old001, File.ReadAllBytes(final001));

      // Инвариант 3: final002 отсутствует (restore отказал), final003 не затронут.
      Assert.False(File.Exists(final002),
          $"Инвариант 3: final002 должен отсутствовать (restore backupB отказал). Журнал: {moveLog}");
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Инвариант 3 (продолжение): уцелевший backup для final002 существует.
      string backupB = fake.MoveCalls[1].Destination;
      Assert.True(File.Exists(backupB),
          $"Инвариант 3: backupB ({backupB}) должен существовать. Журнал: {moveLog}");
      Assert.Equal(old002, File.ReadAllBytes(backupB));

      // Инвариант 4: публикации не было — ни один final не содержит new-*.
      Assert.DoesNotContain("new-", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.DoesNotContain("new-", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Инвариант 6: уцелевшая копия backupB не удалена.
      Assert.DoesNotContain(backupB, fake.DeleteCalls);

      // Инвариант 7: повторный Commit запрещён (retry).
      int movesBefore = fake.MoveCalls.Count;
      int deletesBefore = fake.DeleteCalls.Count;
      Assert.Throws<InvalidOperationException>(() => set.Commit());
      Assert.Equal(movesBefore, fake.MoveCalls.Count);
      Assert.Equal(deletesBefore, fake.DeleteCalls.Count);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Сбой на ПЕРВОМ publish Move после успешной backup-фазы: новые тома не публикуются,
  /// исходный набор восстанавливается байт-в-байт.
  /// </summary>
  [Fact]
  public void Commit_MoveFailureBeforeFirstPublishedVolume_RestoresOriginalSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Три backup Move (#0–#2) проходят, первый publish Move (#3) падает.
    var fake = new StagedFileOperationsFake(failMoveIndex: 3);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));
      File.WriteAllBytes(staged003, Encoding.UTF8.GetBytes("new-003"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      Assert.Throws<IOException>(() => set.Commit());

      // Все исходные тома восстановлены байт-в-байт.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Ни один конечный том не содержит new-* данных.
      Assert.Equal("old-001", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.Equal("old-002", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.Equal("old-003", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Посторонний файл не тронут.
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));

      // Префикс Move: #0–#2 backup, #3 — первый publish (сбой).
      Assert.True(fake.MoveCalls.Count >= 4, "Ожидались минимум 4 вызова Move.");
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(final002, fake.MoveCalls[1].Source);
      Assert.Equal(final003, fake.MoveCalls[2].Source);
      Assert.Equal(staged001, fake.MoveCalls[3].Source);
      Assert.Equal(final001, fake.MoveCalls[3].Destination);

      // Backup-копии после успешного rollback не остаются.
      string backupA = fake.MoveCalls[0].Destination;
      string backupB = fake.MoveCalls[1].Destination;
      string backupC = fake.MoveCalls[2].Destination;

      Assert.Equal(dir, Path.GetDirectoryName(backupA));
      Assert.Equal(dir, Path.GetDirectoryName(backupB));
      Assert.Equal(dir, Path.GetDirectoryName(backupC));
      Assert.NotEqual(backupA, backupB);
      Assert.NotEqual(backupA, backupC);
      Assert.NotEqual(backupB, backupC);

      Assert.False(File.Exists(backupA));
      Assert.False(File.Exists(backupB));
      Assert.False(File.Exists(backupC));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Сбой на ПОСЛЕДНЕМ publish Move: первые два новых тома публикуются, третий падает.
  /// Оба опубликованных тома удаляются, исходный набор восстанавливается байт-в-байт.
  /// </summary>
  [Fact]
  public void Commit_MoveFailureOnLastPublish_RestoresOriginalSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Три backup Move (#0–#2) и первые два publish Move (#3–#4) проходят,
    // третий publish Move (#5) падает.
    var fake = new StagedFileOperationsFake(failMoveIndex: 5);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));
      File.WriteAllBytes(staged003, Encoding.UTF8.GetBytes("new-003"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      Assert.Throws<IOException>(() => set.Commit());

      // Все исходные тома восстановлены байт-в-байт.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Ни один конечный том не содержит new-* данных: опубликованные тома удалены.
      Assert.Equal("old-001", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.Equal("old-002", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.Equal("old-003", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      // Посторонний файл не тронут.
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));

      // Префикс Move: #0–#2 backup, #3–#4 publish (успех), #5 publish (сбой).
      Assert.True(fake.MoveCalls.Count >= 6, "Ожидались минимум 6 вызовов Move.");
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(final002, fake.MoveCalls[1].Source);
      Assert.Equal(final003, fake.MoveCalls[2].Source);

      Assert.Equal(staged001, fake.MoveCalls[3].Source);
      Assert.Equal(final001, fake.MoveCalls[3].Destination);
      Assert.Equal(staged002, fake.MoveCalls[4].Source);
      Assert.Equal(final002, fake.MoveCalls[4].Destination);
      Assert.Equal(staged003, fake.MoveCalls[5].Source);
      Assert.Equal(final003, fake.MoveCalls[5].Destination);

      // Backup-копии после успешного rollback не остаются.
      string backupA = fake.MoveCalls[0].Destination;
      string backupB = fake.MoveCalls[1].Destination;
      string backupC = fake.MoveCalls[2].Destination;

      Assert.Equal(dir, Path.GetDirectoryName(backupA));
      Assert.Equal(dir, Path.GetDirectoryName(backupB));
      Assert.Equal(dir, Path.GetDirectoryName(backupC));
      Assert.NotEqual(backupA, backupB);
      Assert.NotEqual(backupA, backupC);
      Assert.NotEqual(backupB, backupC);

      Assert.False(File.Exists(backupA));
      Assert.False(File.Exists(backupB));
      Assert.False(File.Exists(backupC));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Успешный commit: все три новых тома опубликованы байт-в-байт, staged-файлы
  /// перенесены, все journaled operation backups удалены, а Dispose не трогает новый набор.
  /// </summary>
  [Fact]
  public void Commit_Success_DeletesAllOperationBackups()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      byte[] new002 = Encoding.UTF8.GetBytes("new-002");
      byte[] new003 = Encoding.UTF8.GetBytes("new-003");

      File.WriteAllBytes(staged001, new001);
      File.WriteAllBytes(staged002, new002);
      File.WriteAllBytes(staged003, new003);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      set.Commit();

      // Каждый конечный том содержит точные новые байты.
      Assert.Equal(new001, File.ReadAllBytes(final001));
      Assert.Equal(new002, File.ReadAllBytes(final002));
      Assert.Equal(new003, File.ReadAllBytes(final003));

      // Staged-тома перенесены и не остаются.
      Assert.False(File.Exists(staged001));
      Assert.False(File.Exists(staged002));
      Assert.False(File.Exists(staged003));

      // Backup phase (#0–#2) + publish (#3–#5): ровно шесть Move.
      Assert.Equal(6, fake.MoveCalls.Count);

      string backupA = fake.MoveCalls[0].Destination;
      string backupB = fake.MoveCalls[1].Destination;
      string backupC = fake.MoveCalls[2].Destination;

      // Cleanup удалил все три journaled backups и не удалял ничего другого.
      Assert.Equal(3, fake.DeleteCalls.Count);
      Assert.Contains(backupA, fake.DeleteCalls);
      Assert.Contains(backupB, fake.DeleteCalls);
      Assert.Contains(backupC, fake.DeleteCalls);

      Assert.False(File.Exists(backupA));
      Assert.False(File.Exists(backupB));
      Assert.False(File.Exists(backupC));

      // После dispose опубликованный новый набор остаётся байт-в-байт.
      set.Dispose();

      Assert.True(File.Exists(final001));
      Assert.True(File.Exists(final002));
      Assert.True(File.Exists(final003));
      Assert.Equal(new001, File.ReadAllBytes(final001));
      Assert.Equal(new002, File.ReadAllBytes(final002));
      Assert.Equal(new003, File.ReadAllBytes(final003));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Controlled отказ Delete при cleanup backups после commit: новый набор не откатывается,
  /// cleanup продолжается для остальных backups, Dispose не удаляет опубликованный набор.
  /// </summary>
  [Fact]
  public void Commit_BackupCleanupDeleteFailure_DoesNotRollbackPublishedSet()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Первый cleanup-Delete (первый backup в журнале) выбрасывает IOException.
    var fake = new StagedFileOperationsFake(failDeleteIndex: 0);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      string stagedBase = Path.Combine(dir, "archive.staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      string staged003 = stagedBase + ".003";

      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      byte[] new002 = Encoding.UTF8.GetBytes("new-002");
      byte[] new003 = Encoding.UTF8.GetBytes("new-003");

      File.WriteAllBytes(staged001, new001);
      File.WriteAllBytes(staged002, new002);
      File.WriteAllBytes(staged003, new003);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002, staged003]);

      // Cleanup failure — best-effort: Commit не сообщает транзакционный сбой.
      set.Commit();

      // Новый набор опубликован и не откачен.
      Assert.Equal(new001, File.ReadAllBytes(final001));
      Assert.Equal(new002, File.ReadAllBytes(final002));
      Assert.Equal(new003, File.ReadAllBytes(final003));

      // Старые байты не восстановлены ни в одном конечном томе.
      Assert.DoesNotContain("old-", Encoding.UTF8.GetString(File.ReadAllBytes(final001)));
      Assert.DoesNotContain("old-", Encoding.UTF8.GetString(File.ReadAllBytes(final002)));
      Assert.DoesNotContain("old-", Encoding.UTF8.GetString(File.ReadAllBytes(final003)));

      string backupA = fake.MoveCalls[0].Destination;
      string backupB = fake.MoveCalls[1].Destination;
      string backupC = fake.MoveCalls[2].Destination;

      // Cleanup продолжился после контролируемого отказа: Delete вызван для всех трёх backups.
      Assert.Equal(3, fake.DeleteCalls.Count);

      // Остальные backups удалены; инжектированный backupA может остаться на диске.
      Assert.False(File.Exists(backupB));
      Assert.False(File.Exists(backupC));

      // Dispose не удаляет опубликованный новый набор.
      set.Dispose();

      Assert.True(File.Exists(final001));
      Assert.True(File.Exists(final002));
      Assert.True(File.Exists(final003));
      Assert.Equal(new001, File.ReadAllBytes(final001));
      Assert.Equal(new002, File.ReadAllBytes(final002));
      Assert.Equal(new003, File.ReadAllBytes(final003));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M5: если первый нумерованный путь сразу за новым manifest
  /// (<c>archive.002</c>) уже существует, его ownership не доказан, и
  /// <see cref="StagedVolumeSet.Commit"/> отклоняет операцию контролируемым
  /// исключением (производное от <see cref="IOException"/>) ДО первой мутации
  /// назначения. Существующие конечные файлы остаются байт-в-байт неизменными;
  /// очистке <see cref="StagedVolumeSet.Dispose"/> подлежат только staged-файлы,
  /// принадлежащие текущей операции.
  /// </summary>
  [Fact]
  public void Commit_AdditionalNumberedFileWithoutOwnershipProof_RejectsBeforeMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Без инъекций отказов Move/Delete: ожидается, что мутаций вообще не будет.
    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");
      byte[] old003 = Encoding.UTF8.GetBytes("old-003");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);
      File.WriteAllBytes(final003, old003);

      // Посторонний файл: не должен затрагиваться ни отказом, ни очисткой.
      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      // Ровно один staged-том: manifest = [staged.001], значит archive.002 —
      // первый нумерованный путь вне manifest с неизвестным ownership.
      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      File.WriteAllBytes(staged001, new001);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001]);

      // Будущий контракт: контролируемый конфликт до первой мутации.
      // Production-исключение будет производным от IOException; конкретный тип
      // здесь не называется, чтобы не менять production-код в этом микрошаге.
      Assert.ThrowsAny<IOException>(() => set.Commit());

      // Доказательства «до первой мутации» (проверяются до Dispose).
      Assert.Empty(fake.MoveCalls);
      Assert.Empty(fake.DeleteCalls);

      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Staged-том ещё существует: отказ не чистит staging — это делает Dispose.
      Assert.True(File.Exists(staged001));

      // Backup-файлы не создавались.
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));

      // В каталоге назначения нет новых финальных данных и лишних файлов:
      // 3 final + 1 staged + 1 посторонний.
      Assert.Equal(5, Directory.GetFiles(dir).Length);

      set.Dispose();

      // После Dispose: staged очищен, существующий набор байт-в-байт цел.
      Assert.False(File.Exists(staged001));

      Assert.True(File.Exists(final001));
      Assert.True(File.Exists(final002));
      Assert.True(File.Exists(final003));
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Equal(old003, File.ReadAllBytes(final003));

      // Backup-файлов нет; посторонний файл не удалён.
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M5.4 (characterization): если нумерованный файл существует только
  /// после разрыва нумерации (<c>archive.003</c> при отсутствии <c>archive.002</c>),
  /// он не детектируется pre-mutation probe и не блокирует успешный commit. Такие
  /// файлы остаются байт-в-байт неизменными; ownership не доказывается и не
  /// требуется. Scan за разрывом не выполняется.
  /// </summary>
  [Fact]
  public void Commit_ExtraVolumeAfterGap_IsPreservedAndCommitSucceeds()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      // archive.001 — в управляемом диапазоне manifest (заменяется).
      // archive.002 — НЕ создаётся (разрыв нумерации).
      // archive.003 — существует после разрыва (orphan, ownership недоказан).
      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";
      string final003 = destinationBase + ".003";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] orphan003 = Encoding.UTF8.GetBytes("orphan-003");

      File.WriteAllBytes(final001, old001);
      // final002 намеренно не создаётся.
      File.WriteAllBytes(final003, orphan003);

      // Посторонний файл: не должен затрагиваться операцией.
      string unrelatedPath = Path.Combine(dir, "unrelated.txt");
      byte[] unrelated = Encoding.UTF8.GetBytes("unrelated-data");
      File.WriteAllBytes(unrelatedPath, unrelated);

      // Ровно один staged-том: manifest = [staged.001].
      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      File.WriteAllBytes(staged001, new001);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001]);

      // Commit должен успешно завершиться: archive.002 отсутствует → конфликта нет.
      set.Commit();

      // archive.001 заменён на new-001.
      Assert.True(File.Exists(final001));
      Assert.Equal(new001, File.ReadAllBytes(final001));

      // archive.002 по-прежнему не существует.
      Assert.False(File.Exists(final002));

      // archive.003 байт-в-байт сохранён (orphan не тронут).
      Assert.True(File.Exists(final003));
      Assert.Equal(orphan003, File.ReadAllBytes(final003));

      // Посторонний файл неизменён.
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));

      // Staged-том удалён (перенесён в final).
      Assert.False(File.Exists(staged001));

      // Backup-файлов не осталось.
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));

      // Состав каталога: ровно 3 файла: final001, final003 и unrelated
      // (staged-том перенесён в final и отсутствует).
      string[] expectedFiles = [final001, final003, unrelatedPath];
      Assert.Equal(expectedFiles.OrderBy(p => p), Directory.GetFiles(dir).OrderBy(p => p));

      // Через журнал операций доказываем, что archive.003 никогда не был:
      // - источником Move;
      // - назначением Move;
      // - целью Delete.
      Assert.DoesNotContain(final003, fake.MoveCalls.Select(c => c.Source));
      Assert.DoesNotContain(final003, fake.MoveCalls.Select(c => c.Destination));
      Assert.DoesNotContain(final003, fake.DeleteCalls);

      // Dispose: финалы остаются байт-в-байт, артефактов нет.
      set.Dispose();

      Assert.Equal(new001, File.ReadAllBytes(final001));
      Assert.Equal(orphan003, File.ReadAllBytes(final003));
      Assert.Equal(unrelated, File.ReadAllBytes(unrelatedPath));
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));
      Assert.Empty(Directory.GetFiles(dir, "staged.*"));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.1A (красный): повторный <see cref="StagedVolumeSet.Commit"/> после
  /// успешного первого commit обязан отклонить операцию с
  /// <see cref="InvalidOperationException"/> и не выполнить ни одной дополнительной
  /// файловой операции. На текущей реализации второй Commit вызывает backup+publish
  /// (с отказом и rollback), тратя Move/Delete-вызовы и создавая окно, в котором
  /// finals временно отсутствуют, поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void Commit_Twice_SecondCallThrowsWithoutFileMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");
      string final001 = destinationBase + ".001";
      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      File.WriteAllBytes(final001, old001);

      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      File.WriteAllBytes(staged001, new001);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001]);

      set.Commit();

      // Снимок после первого успешного commit.
      int moveCallsAfterFirst = fake.MoveCalls.Count;
      int deleteCallsAfterFirst = fake.DeleteCalls.Count;
      byte[] finalBytesAfterFirst = File.ReadAllBytes(final001);
      string[] filesAfterFirst = Directory.GetFiles(dir).OrderBy(p => p).ToArray();

      // Повторный Commit: ожидается InvalidOperationException, без файловых мутаций.
      Assert.Throws<InvalidOperationException>(() => set.Commit());

      Assert.Equal(moveCallsAfterFirst, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsAfterFirst, fake.DeleteCalls.Count);
      Assert.Equal(finalBytesAfterFirst, File.ReadAllBytes(final001));
      Assert.Equal(filesAfterFirst, Directory.GetFiles(dir).OrderBy(p => p).ToArray());
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.1A (красный): после контролируемого отказа publish и rollback
  /// повторный <see cref="StagedVolumeSet.Commit"/> обязан отклонить операцию с
  /// <see cref="InvalidOperationException"/> и не выполнить ни одной дополнительной
  /// файловой операции. Транзакция one-shot: даже чистый rollback не открывает
  /// повтор. На текущей реализации retry разрешён — test доказуемо падает.
  /// </summary>
  [Fact]
  public void Commit_AfterControlledFailure_SecondCallThrowsWithoutFileMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // backup Move = index 0 (успех), publish Move = index 1 (сбой IOException).
    var fake = new StagedFileOperationsFake(failMoveIndex: 1);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");
      string final001 = destinationBase + ".001";
      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      File.WriteAllBytes(final001, old001);

      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      File.WriteAllBytes(staged001, new001);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001]);

      // Первый Commit падает контролируемо (IOException из publish Move).
      Assert.Throws<IOException>(() => set.Commit());

      // Существующий rollback обязан восстановить исходные байты.
      Assert.Equal(old001, File.ReadAllBytes(final001));

      // Снимок после первого отказа и rollback.
      int moveCallsAfterFailure = fake.MoveCalls.Count;
      int deleteCallsAfterFailure = fake.DeleteCalls.Count;

      // Повторный Commit: ожидается InvalidOperationException, без файловых мутаций.
      Assert.Throws<InvalidOperationException>(() => set.Commit());

      Assert.Equal(moveCallsAfterFailure, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsAfterFailure, fake.DeleteCalls.Count);
      Assert.Equal(old001, File.ReadAllBytes(final001));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.1A (красный): после <see cref="StagedVolumeSet.Dispose"/> любые
  /// мутающие операции (<see cref="StagedVolumeSet.Commit"/> и
  /// <see cref="StagedVolumeSet.SetVolumes"/>) обязаны отклонять вызов с
  /// <see cref="ObjectDisposedException"/>, не выполняя ни одной файловой операции.
  /// Read-only свойства (DestinationBasePath, StagedBasePath, Manifest) остаются
  /// читаемыми. На текущей реализации disposed-флага нет, поэтому тест доказуемо
  /// падает: Commit бросает IOException, SetVolumes проходит без исключения.
  /// </summary>
  [Fact]
  public void Operations_AfterDispose_ThrowObjectDisposedException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    try
    {
      // Instance A: Dispose после SetVolumes; Commit обязан бросить ODE
      // без дополнительных Move/Delete.
      {
        var fakeA = new StagedFileOperationsFake();
        string destinationBaseA = Path.Combine(dir, "archiveA");
        string stagedBaseA = Path.Combine(dir, "stagedA");
        string staged001A = stagedBaseA + ".001";
        File.WriteAllBytes(staged001A, Encoding.UTF8.GetBytes("new-a-001"));

        var setA = new StagedVolumeSet(destinationBaseA, fakeA);
        setA.SetVolumes([staged001A]);
        setA.Dispose();

        int moveA = fakeA.MoveCalls.Count;
        int deleteA = fakeA.DeleteCalls.Count;

        Assert.Throws<ObjectDisposedException>(() => setA.Commit());

        Assert.Equal(moveA, fakeA.MoveCalls.Count);
        Assert.Equal(deleteA, fakeA.DeleteCalls.Count);

        // Read-only свойства остаются читаемыми после Dispose.
        _ = setA.DestinationBasePath;
        _ = setA.StagedBasePath;
        _ = setA.Manifest;
      }

      // Instance B: Dispose до SetVolumes; SetVolumes обязан бросить ODE
      // без единой Move/Delete-операции.
      {
        var fakeB = new StagedFileOperationsFake();
        string destinationBaseB = Path.Combine(dir, "archiveB");
        string stagedBaseB = Path.Combine(dir, "stagedB");
        string staged001B = stagedBaseB + ".001";
        File.WriteAllBytes(staged001B, Encoding.UTF8.GetBytes("new-b-001"));

        var setB = new StagedVolumeSet(destinationBaseB, fakeB);
        setB.Dispose();

        Assert.Throws<ObjectDisposedException>(() => setB.SetVolumes([staged001B]));

        Assert.Empty(fakeB.MoveCalls);
        Assert.Empty(fakeB.DeleteCalls);

        _ = setB.DestinationBasePath;
        _ = setB.StagedBasePath;
        _ = setB.Manifest;
      }
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.2A (красный): пустой список staged-путей должен отклоняться
  /// <see cref="ArgumentException"/> до любых файловых операций. Текущая реализация
  /// принимает пустой список (Count остаётся 0), поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void SetVolumes_Empty_ThrowsArgumentException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      using var set = new StagedVolumeSet(destinationBase, fake);

      // Пустой список: ожидается ArgumentException.
      Assert.Throws<ArgumentException>(() => set.SetVolumes([]));

      // Ни одной файловой операции.
      Assert.Empty(fake.MoveCalls);
      Assert.Empty(fake.DeleteCalls);

      // Состояние остаётся Created: повторный вызов также отклоняется.
      Assert.Throws<ArgumentException>(() => set.SetVolumes([]));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.2A (красный): список с null или whitespace элементами должен отклоняться
  /// <see cref="ArgumentException"/> до любых файловых операций. Текущая реализация
  /// принимает такие пути (AddRange без валидации), что приводит к отказам mid-publish
  /// после backup-фазы, поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void SetVolumes_NullOrWhitespacePath_ThrowsArgumentException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      // Тест 1: null-элемент в списке.
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        Assert.Throws<ArgumentException>(() => set.SetVolumes([null!]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }

      // Тест 2: пустая строка.
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        Assert.Throws<ArgumentException>(() => set.SetVolumes([""]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }

      // Тест 3: whitespace.
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        Assert.Throws<ArgumentException>(() => set.SetVolumes(["   "]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.2A (красный): дубликаты staged-путей (точная копия и лексически
  /// эквивалентные с разным написанием) должны отклоняться <see cref="ArgumentException"/>
  /// до любых файловых операций. Текущая реализация не проверяет уникальность,
  /// что приводит к повторным Move одного staged-файла mid-publish (после backup),
  /// поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void SetVolumes_DuplicatePaths_ThrowsArgumentException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      // Тест 1: точный дубликат.
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        string staged001 = Path.Combine(dir, "staged.001");
        Assert.Throws<ArgumentException>(() => set.SetVolumes([staged001, staged001]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }

      // Тест 2: лексически эквивалентные пути с разным написанием.
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        string staged001 = Path.Combine(dir, "staged.001");
        string staged001Dot = Path.Combine(dir, ".", "staged.001");
        Assert.Throws<ArgumentException>(() => set.SetVolumes([staged001, staged001Dot]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.2A (красный): staged-путь, совпадающий с одним из финальных путей
  /// (вычисляемых из destinationBase и manifest count), должен отклоняться
  /// <see cref="ArgumentException"/> до любых файловых операций. Текущая реализация
  /// не проверяет коллизии, что приводит к исчезновению staged-источника после backup
  /// (backup уводит final = staged в .bak), поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void SetVolumes_PathCollidesWithFinal_ThrowsArgumentException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      // Тест 1: staged-путь совпадает с final.001 (первый финальный путь).
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        string final001 = destinationBase + ".001";
        Assert.Throws<ArgumentException>(() => set.SetVolumes([final001]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }

      // Тест 2: staged-путь совпадает с final.002 (второй финальный путь при 2 томах).
      {
        using var set = new StagedVolumeSet(destinationBase, fake);
        string staged001 = Path.Combine(dir, "staged.001");
        string final002 = destinationBase + ".002";
        Assert.Throws<ArgumentException>(() => set.SetVolumes([staged001, final002]));
        Assert.Empty(fake.MoveCalls);
        Assert.Empty(fake.DeleteCalls);
      }
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.3A (красный): если manifest ссылается на отсутствующий staged-файл,
  /// <see cref="StagedVolumeSet.Commit"/> обязан отклонить операцию
  /// <see cref="FileNotFoundException"/> ДО первой мутации назначения. На текущей
  /// реализации backup-фаза сначала уводит finals в .bak, и только затем publish
  /// обнаруживает отсутствующий источник (с rollback), поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void Commit_MissingStagedFile_RejectsBeforeMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      // Два старых финальных тома с различающимися байтами.
      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);

      // staged.001 существует; staged.002 включён в manifest, но НЕ создаётся.
      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";
      byte[] new001 = Encoding.UTF8.GetBytes("new-001");
      File.WriteAllBytes(staged001, new001);

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002]);

      // Будущий контракт: FileNotFoundException до первой мутации.
      Assert.Throws<FileNotFoundException>(() => set.Commit());

      // Ни одной Move/Delete-операции: назначение не затронуто.
      Assert.Empty(fake.MoveCalls);
      Assert.Empty(fake.DeleteCalls);

      // Старые finals байт-в-байт прежние; backup не создавались.
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));

      // staged.001 существует до Dispose: отказ не чистит staging.
      Assert.True(File.Exists(staged001));

      set.Dispose();

      // После Dispose: staged.001 удалён; назначение байт-в-байт неизменно.
      Assert.False(File.Exists(staged001));
      Assert.Equal(old001, File.ReadAllBytes(final001));
      Assert.Equal(old002, File.ReadAllBytes(final002));
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6.4A (красный): не-контролируемое исключение в backup-фазе обязано
  /// пробрасываться как есть, БЕЗ попытки controlled-отката (RestoreBackups) и БЕЗ
  /// права на повторный Commit. Тест фиксирует, что M6 запрещает retry после
  /// не-контролируемого отказа; полное поведение восстановления — задача M7.
  /// На текущей реализации широкий <c>catch (Exception)</c> в backup-фазе пытается
  /// выполнить RestoreBackups, поэтому тест доказуемо падает.
  /// </summary>
  [Fact]
  public void Commit_BackupNonControlledFailure_EscapesWithoutRestoreAndForbidsRetry()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    // Move #0 — backup final001 (успех); Move #1 — backup final002: не-контролируемое
    // исключение ДО переноса.
    var injected = new InvalidOperationException("Injected non-controlled backup failure.");
    var fake = new StagedFileOperationsFake(failMoveIndex: 1, moveFailure: injected);

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string final001 = destinationBase + ".001";
      string final002 = destinationBase + ".002";

      byte[] old001 = Encoding.UTF8.GetBytes("old-001");
      byte[] old002 = Encoding.UTF8.GetBytes("old-002");

      File.WriteAllBytes(final001, old001);
      File.WriteAllBytes(final002, old002);

      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      string staged002 = stagedBase + ".002";

      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));
      File.WriteAllBytes(staged002, Encoding.UTF8.GetBytes("new-002"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001, staged002]);

      // 1. Первый Commit пробрасывает то же самое не-контролируемое исключение
      //    (без обёртки в AggregateException).
      Assert.Same(injected, Assert.Throws<InvalidOperationException>(() => set.Commit()));

      // 2–3. Restore НЕ выполняется: в журнале ровно успешный backup #0
      //      и неудачная попытка backup #1.
      Assert.Equal(2, fake.MoveCalls.Count);
      Assert.Equal(final001, fake.MoveCalls[0].Source);
      Assert.Equal(final002, fake.MoveCalls[1].Source);
      Assert.Equal(dir, Path.GetDirectoryName(fake.MoveCalls[0].Destination));
      Assert.Equal(dir, Path.GetDirectoryName(fake.MoveCalls[1].Destination));

      // 4. Backup, созданный Move #0, остаётся на диске (не восстановлен).
      string backup001 = fake.MoveCalls[0].Destination;
      Assert.True(File.Exists(backup001));

      // 5. final001 отсутствует: controlled rollback не выполнялся.
      Assert.False(File.Exists(final001));

      // 6. final002 не тронут.
      Assert.Equal(old002, File.ReadAllBytes(final002));

      // Публикации не было: staged-тома на месте.
      Assert.True(File.Exists(staged001));
      Assert.True(File.Exists(staged002));

      // 7. Повторный Commit запрещён (lifecycle Failed) и не выполняет
      //    ни одной новой Move/Delete-операции.
      int moveCallsAfterFailure = fake.MoveCalls.Count;
      int deleteCallsAfterFailure = fake.DeleteCalls.Count;

      Assert.Throws<InvalidOperationException>(() => set.Commit());

      Assert.Equal(moveCallsAfterFailure, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsAfterFailure, fake.DeleteCalls.Count);
    }
    finally
    {
      // 8. Очистка временного каталога; ошибки очистки не маскируют падение теста.
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6 pin: повторный <see cref="StagedVolumeSet.SetVolumes"/> отклоняется
  /// <see cref="InvalidOperationException"/> без единой файловой операции, а manifest
  /// сохраняет ровно первый список.
  /// </summary>
  [Fact]
  public void SetVolumes_Twice_ThrowsWithoutFileMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      using var set = new StagedVolumeSet(destinationBase, fake);

      // Два различных валидных manifest: непустые, уникальные, без коллизий с finals.
      string[] firstManifest =
      [
          Path.Combine(dir, "stagedA.001"),
          Path.Combine(dir, "stagedA.002"),
      ];

      string[] secondManifest =
      [
          Path.Combine(dir, "stagedB.001"),
          Path.Combine(dir, "stagedB.002"),
      ];

      set.SetVolumes(firstManifest);

      int moveCallsAfterFirst = fake.MoveCalls.Count;
      int deleteCallsAfterFirst = fake.DeleteCalls.Count;

      Assert.Throws<InvalidOperationException>(() => set.SetVolumes(secondManifest));

      // Число операций не изменилось.
      Assert.Equal(moveCallsAfterFirst, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsAfterFirst, fake.DeleteCalls.Count);

      // Manifest содержит ровно первый список; элементы второго не добавлены.
      Assert.Equal(firstManifest, set.Manifest);

      // В каталоге назначения ничего не создано, не перенесено и не удалено.
      Assert.Empty(Directory.GetFiles(dir));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6 pin: <see cref="StagedVolumeSet.Commit"/> без
  /// <see cref="StagedVolumeSet.SetVolumes"/> отклоняется
  /// <see cref="InvalidOperationException"/> до любых файловых операций:
  /// backup и конечные тома не создаются, после чего объект штатно диспозируется.
  /// </summary>
  [Fact]
  public void Commit_BeforeSetVolumes_ThrowsInvalidOperationExceptionWithoutFileMutation()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");
      string final001 = destinationBase + ".001";

      using var set = new StagedVolumeSet(destinationBase, fake);

      int moveCallsBefore = fake.MoveCalls.Count;
      int deleteCallsBefore = fake.DeleteCalls.Count;

      Assert.Throws<InvalidOperationException>(() => set.Commit());

      // Число операций не изменилось.
      Assert.Equal(moveCallsBefore, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsBefore, fake.DeleteCalls.Count);

      // Backup не создавались, конечный том не публиковался.
      Assert.Empty(Directory.GetFiles(dir, "*.bak"));
      Assert.False(File.Exists(final001));

      // После отказа объект штатно диспозируется и ничего не создаёт.
      set.Dispose();

      Assert.Empty(Directory.GetFiles(dir));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-M6-F1A (красный): приоритет disposed-состояния над проверкой аргумента в
  /// <see cref="StagedVolumeSet.SetVolumes"/>. Согласованный контракт M6 (README pack,
  /// правило после Dispose; 02_M6_1B, раздел SetVolumes: «If <c>Disposed</c>: throw
  /// <see cref="ObjectDisposedException"/>») не делает исключения для <c>null</c>-манифеста.
  /// На текущей реализации <c>ArgumentNullException.ThrowIfNull</c> выполняется раньше
  /// disposed-guard, поэтому <see cref="StagedVolumeSet.SetVolumes"/>(null) после Dispose
  /// бросает <see cref="ArgumentNullException"/> — Disposed-сценарий доказуемо падает.
  /// Контрольный Created-блок фиксирует, что null-валидация аргумента сохраняется и не
  /// мешает приёму последующего корректного манифеста. <see cref="StagedFileOperationsFake"/>
  /// считает только Move/Delete; утверждения ниже не измеряют Exists.
  /// </summary>
  [Fact]
  public void SetVolumes_NullAfterDispose_ThrowsObjectDisposedException()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    try
    {
      // Контроль Created: до Dispose null-манифест отклоняется точным
      // ArgumentNullException, объект остаётся пригодным для корректного списка.
      {
        var fakeCreated = new StagedFileOperationsFake();
        string destinationBase = Path.Combine(dir, "archiveCreated");
        string stagedBase = Path.Combine(dir, "stagedCreated");
        string staged001 = stagedBase + ".001";
        File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-created-001"));

        using var setCreated = new StagedVolumeSet(destinationBase, fakeCreated);

        Assert.Throws<ArgumentNullException>(() => setCreated.SetVolumes(null!));

        // Отклонённый null-вызов не выполнил ни одной Move/Delete-операции.
        Assert.Empty(fakeCreated.MoveCalls);
        Assert.Empty(fakeCreated.DeleteCalls);
        Assert.Empty(setCreated.Manifest);

        // Состояние осталось Created: корректный непустой манифест по-прежнему принимается.
        // Commit намеренно не вызывается — проверяется только приём манифеста.
        string[] createdManifest = [staged001];
        setCreated.SetVolumes(createdManifest);

        Assert.Equal(createdManifest, setCreated.Manifest);
        Assert.Empty(fakeCreated.MoveCalls);
        Assert.Empty(fakeCreated.DeleteCalls);
      }

      // Основной Disposed-сценарий: Dispose выполняется первым, снимки снимаются только
      // после него, затем null-манифест обязан дать точный ObjectDisposedException.
      {
        var fakeDisposed = new StagedFileOperationsFake();
        string destinationBase = Path.Combine(dir, "archiveDisposed");

        var setDisposed = new StagedVolumeSet(destinationBase, fakeDisposed);
        setDisposed.Dispose();

        int moveCallsAfterDispose = fakeDisposed.MoveCalls.Count;
        int deleteCallsAfterDispose = fakeDisposed.DeleteCalls.Count;
        string[] manifestAfterDispose = setDisposed.Manifest.ToArray();
        string destinationBaseAfterDispose = setDisposed.DestinationBasePath;
        string stagedBaseAfterDispose = setDisposed.StagedBasePath;

        Assert.Throws<ObjectDisposedException>(() => setDisposed.SetVolumes(null!));

        // Отклонённый вызов не добавил Move/Delete-операций.
        Assert.Equal(moveCallsAfterDispose, fakeDisposed.MoveCalls.Count);
        Assert.Equal(deleteCallsAfterDispose, fakeDisposed.DeleteCalls.Count);

        // Read-only свойства остаются читаемыми и неизменными после Dispose.
        Assert.Equal(destinationBaseAfterDispose, setDisposed.DestinationBasePath);
        Assert.Equal(stagedBaseAfterDispose, setDisposed.StagedBasePath);
        Assert.Equal(manifestAfterDispose, setDisposed.Manifest);
      }
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// SEC002-PREM7-F2A (красный): опубликованное значение <see cref="StagedVolumeSet.Manifest"/>
  /// не должно позволять изменять фактический backing manifest через приведение к изменяемому
  /// интерфейсу (F2/AUD-004). Контракт проверяется только через опубликованное значение:
  /// конкретный runtime-тип не фиксируется. На базовой реализации свойство возвращает живой
  /// <c>List&lt;string&gt;</c>, поэтому <c>Add</c> проходит без <see cref="NotSupportedException"/>
  /// и тест доказуемо падает.
  /// </summary>
  [Fact]
  public void SetVolumes_ManifestCannotBeMutatedThroughCast()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-staged-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);

    var fake = new StagedFileOperationsFake();

    try
    {
      string destinationBase = Path.Combine(dir, "archive");

      string stagedBase = Path.Combine(dir, "staged");
      string staged001 = stagedBase + ".001";
      File.WriteAllBytes(staged001, Encoding.UTF8.GetBytes("new-001"));

      // Второй корректный staged-путь: не дубликат и не коллизия с финальными путями.
      string stagedExtra = Path.Combine(dir, "staged-extra.001");
      File.WriteAllBytes(stagedExtra, Encoding.UTF8.GetBytes("extra-001"));

      using var set = new StagedVolumeSet(destinationBase, fake);
      set.SetVolumes([staged001]);

      IReadOnlyList<string> manifest = set.Manifest;
      IList<string> mutableView = Assert.IsAssignableFrom<IList<string>>(manifest);

      int moveCallsBefore = fake.MoveCalls.Count;
      int deleteCallsBefore = fake.DeleteCalls.Count;

      // Мутация через опубликованное значение обязана отклоняться точным NotSupportedException.
      Assert.Throws<NotSupportedException>(() => mutableView.Add(stagedExtra));

      // Отклонённая мутация не изменила manifest и не выполнила файловых операций.
      string[] expectedManifest = [staged001];
      Assert.Equal(expectedManifest, set.Manifest);
      Assert.Equal(moveCallsBefore, fake.MoveCalls.Count);
      Assert.Equal(deleteCallsBefore, fake.DeleteCalls.Count);

      // Commit намеренно не вызывается: проверяется только неизменяемость manifest.
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// F3 / AUD-005 (validate/use consistency): относительные staged-пути нормализуются в
  /// <see cref="StagedVolumeSet.SetVolumes"/> относительно CWD на момент валидации, а файловые
  /// операции <see cref="StagedVolumeSet.Commit"/> выполняют I/O по исходным строкам manifest
  /// относительно CWD на момент коммита. Если CWD изменился между вызовами, проверенное
  /// множество путей не совпадает с множеством, над которым выполняется I/O.
  ///
  /// Сценарий исполняется в отдельном процессе-пробе (<c>tests/Lzma.Ui.CwdProbe</c>), поэтому
  /// глобальный CWD xUnit-раннера не меняется. Инварианты проверяются по фактическому состоянию
  /// файловой системы в этом тесте, а не по сообщениям production-кода.
  ///
  /// Замечание (ревью A1-R, §5): проба передаёт в <see cref="StagedVolumeSet.SetVolumes"/>
  /// относительные staged-пути напрямую, поэтому <see cref="StagedVolumeSet.StagedBasePath"/>,
  /// производный от <c>destinationBasePath</c>, не совпадает с фактическим расположением
  /// staged-файлов. Это допустимо для изолированного теста класса и сознательно НЕ повторяет
  /// production-поток, где staged-тома создаёт <c>VolumeSpanningWriteStream</c>
  /// (<c>LzmaArchiveService.cs:226</c>).
  ///
  /// Destination в сценарии — абсолютный путь в проверенном каталоге. Причина: решением F3-Q3
  /// destination-сторона (<c>_destinationBasePath</c>, final/backup/conflict/probe-пути) в фиксе
  /// НЕ замораживается, поэтому тест обязан изолировать ровно тот дефект, который закрывает
  /// вариант B: validate/use inconsistency относительных staged-путей manifest.
  /// </summary>
  [Fact]
  public void SetVolumes_RelativePaths_UseFrozenValidatedPathIdentity()
  {
    string root = Path.Combine(Path.GetTempPath(), "lzmasharp-f3-cwd-" + Guid.NewGuid().ToString("N"));
    string validatedDir = Path.Combine(root, "validated");
    string switchedDir = Path.Combine(root, "switched");
    Directory.CreateDirectory(validatedDir);
    Directory.CreateDirectory(switchedDir);

    const string stagedName = "staged.001";
    const string publishedName = "archive.7z.001";
    const string destinationBaseName = "archive.7z";

    try
    {
      // Staged-том — в каталоге, относительно которого выполняется валидация;
      // sentinel с тем же относительным именем — в каталоге, на который CWD переключается
      // перед Commit. Совпадение имён — то, что делает дефект наблюдаемым.
      File.WriteAllText(Path.Combine(validatedDir, stagedName), "A1");
      File.WriteAllText(Path.Combine(switchedDir, stagedName), "B1");

      // Destination — абсолютный (см. XML-doc): замораживаются только staged-пути (F3-Q3).
      string destinationBasePath = Path.Combine(validatedDir, destinationBaseName);

      (int exitCode, string stdout, string stderr) = RunCwdProbe(
          LocateCwdProbe(), validatedDir, switchedDir, destinationBasePath, stagedName);

      string evidence =
          $"exit={exitCode}{Environment.NewLine}--- probe stdout ---{Environment.NewLine}{stdout}" +
          $"--- probe stderr ---{Environment.NewLine}{stderr}";

      Assert.True(exitCode == 0,
          $"Проба должна выполнить Commit без исключения (иначе сценарий не воспроизведён).{Environment.NewLine}{evidence}");

      // И1: опубликованный том появился в проверенном каталоге и содержит байты его staged-тома.
      string publishedInValidated = Path.Combine(validatedDir, publishedName);
      Assert.True(File.Exists(publishedInValidated),
          $"И1: том должен быть опубликован в каталоге, который был проверен.{Environment.NewLine}{evidence}");
      Assert.Equal("A1", File.ReadAllText(publishedInValidated));

      // И2: sentinel в непроверенном каталоге не использован как источник и не изменён.
      string sentinel = Path.Combine(switchedDir, stagedName);
      Assert.True(File.Exists(sentinel),
          $"И2: sentinel не должен быть перемещён.{Environment.NewLine}{evidence}");
      Assert.Equal("B1", File.ReadAllText(sentinel));

      // И2б: в непроверенном каталоге не появилось опубликованного тома.
      Assert.False(File.Exists(Path.Combine(switchedDir, publishedName)),
          $"И2б: публикация не должна выполняться относительно нового CWD.{Environment.NewLine}{evidence}");

      // И3: staged-том в проверенном каталоге перенесён, а не скопирован.
      Assert.False(File.Exists(Path.Combine(validatedDir, stagedName)),
          $"И3: staged-том должен быть перенесён.{Environment.NewLine}{evidence}");

      // И4: резервных копий не осталось — публикация не перекрывала существующие тома.
      Assert.Empty(Directory.GetFiles(root, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      try { Directory.Delete(root, recursive: true); }
      catch (IOException) { }
      catch (UnauthorizedAccessException) { }
    }
  }

  /// <summary>
  /// Находит сборку процесса-пробы относительно корня решения. Тихий пропуск запрещён (TST-001):
  /// проба входит в <c>LzmaSharp.sln</c>, поэтому её отсутствие означает, что решение собрано
  /// не целиком, и тест обязан сообщить причину, а не стать ложно-зелёным.
  /// </summary>
  private static string LocateCwdProbe()
  {
    string baseDirectory = AppContext.BaseDirectory;

    DirectoryInfo? solution = new(baseDirectory);
    while (solution is not null && !File.Exists(Path.Combine(solution.FullName, "LzmaSharp.sln")))
    {
      solution = solution.Parent;
    }

    if (solution is null)
    {
      throw new FileNotFoundException(
          $"Не найден корень решения (LzmaSharp.sln) при обходе вверх от {baseDirectory}.");
    }

    string configuration = DetectConfiguration(baseDirectory);

    string probeDirectory = Path.Combine(solution.FullName, "tests", "Lzma.Ui.CwdProbe");
    string probeName = "Lzma.Ui.CwdProbe.dll";

    // Решение собирается с UseArtifactsOutput=true (Directory.Build.props), поэтому основной
    // путь — artifacts/bin/<ProjectName>/<configuration>/. Классическая схема bin/<Config>/net10.0/
    // оставлена запасным вариантом.
    string[] candidates =
    [
      Path.Combine(solution.FullName, "artifacts", "bin", "Lzma.Ui.CwdProbe", configuration, probeName),
      Path.Combine(probeDirectory, "bin", configuration, "net10.0", probeName),
      Path.Combine(probeDirectory, "bin", ToTitleCase(configuration), "net10.0", probeName),
    ];

    foreach (string candidate in candidates)
    {
      if (File.Exists(candidate))
      {
        return candidate;
      }
    }

    // Тихий пропуск запрещён (TST-001): проба входит в решение, поэтому её отсутствие означает,
    // что решение собрано не целиком. Тест обязан сообщить причину, а не стать ложно-зелёным.
    throw new FileNotFoundException(
        $"Проба CWD не собрана. Проверены пути:{Environment.NewLine}  " +
        string.Join($"{Environment.NewLine}  ", candidates) +
        $"{Environment.NewLine}Соберите решение целиком: dotnet build LzmaSharp.sln -c {ToTitleCase(configuration)}.",
        candidates[0]);
  }

  /// <summary>Возвращает имя конфигурации с заглавной буквы (<c>release</c> → <c>Release</c>).</summary>
  private static string ToTitleCase(string configuration) =>
      configuration.Length == 0
          ? configuration
          : char.ToUpperInvariant(configuration[0]) + configuration[1..];

  /// <summary>
  /// Определяет имя конфигурации по сегментам пути сборки. Работает для обеих схем вывода:
  /// <c>artifacts/bin/&lt;Project&gt;/release/</c> (<c>UseArtifactsOutput=true</c> из
  /// <c>Directory.Build.props</c>) и классической <c>bin/Release/net10.0/</c>.
  /// </summary>
  private static string DetectConfiguration(string baseDirectory)
  {
    foreach (string segment in baseDirectory.Split(
                 Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
    {
      if (string.Equals(segment, "release", StringComparison.OrdinalIgnoreCase))
      {
        return "release";
      }

      if (string.Equals(segment, "debug", StringComparison.OrdinalIgnoreCase))
      {
        return "debug";
      }
    }

    return "release";
  }

  /// <summary>
  /// Запускает процесс-пробу с <c>WorkingDirectory = initialCwd</c> и возвращает код возврата
  /// вместе с stdout/stderr для прикладывания к сообщению об отказе. CWD тест-раннера не меняется.
  /// </summary>
  private static (int ExitCode, string StdOut, string StdErr) RunCwdProbe(
      string probeDll,
      string initialCwd,
      string switchCwd,
      string destinationBasePath,
      string stagedPath)
  {
    ProcessStartInfo psi = new("dotnet")
    {
      WorkingDirectory = initialCwd,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    psi.ArgumentList.Add(probeDll);
    psi.ArgumentList.Add(initialCwd);
    psi.ArgumentList.Add(switchCwd);
    psi.ArgumentList.Add(destinationBasePath);
    psi.ArgumentList.Add(stagedPath);

    using Process process = Process.Start(psi)!;
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();

    if (!process.WaitForExit(120_000))
    {
      try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }

      throw new TimeoutException($"Проба CWD не завершилась за 120 с: {probeDll}");
    }

    return (process.ExitCode, stdout, stderr);
  }

  /// <summary>
  /// Fake файловых операций: по умолчанию делегирует <see cref="File"/>, детерминированно
  /// считает вызовы Move/Delete и выбрасывает IOException на точно заданном номере
  /// (нумерация с нуля) в Move или Delete. Для Move отказ можно задать своим
  /// исключением через <c>moveFailure</c> (по умолчанию — IOException).
  /// Поддерживает множество точек отказа Move через дополнительный конструктор.
  /// </summary>
  private sealed class StagedFileOperationsFake : IStagedVolumeFileOperations
  {
    private readonly Dictionary<int, Exception> _failMoveIndices;
    private readonly int _failDeleteIndex;
    private int _moveCallCount;
    private int _deleteCallCount;

    /// <summary>
    /// Базовый конструктор (обратная совместимость): одна точка отказа Move и/или Delete.
    /// </summary>
    public StagedFileOperationsFake(
        int failMoveIndex = -1,
        int failDeleteIndex = -1,
        Exception? moveFailure = null)
    {
      _failDeleteIndex = failDeleteIndex;
      _failMoveIndices = new Dictionary<int, Exception>();
      if (failMoveIndex >= 0)
      {
        _failMoveIndices[failMoveIndex] = moveFailure ?? new IOException($"Injected failure on Move #{failMoveIndex}.");
      }
    }

    /// <summary>
    /// Расширенный конструктор: множество точек отказа Move. Каждый индекс 0-based,
    /// сквозной для всех фаз. Дубликат индекса — <see cref="ArgumentException"/>.
    /// </summary>
    public StagedFileOperationsFake(
        IEnumerable<(int Index, Exception? Failure)> failMoveIndices,
        int failDeleteIndex = -1)
    {
      _failDeleteIndex = failDeleteIndex;
      _failMoveIndices = new Dictionary<int, Exception>();
      foreach (var (index, failure) in failMoveIndices)
      {
        if (index < 0)
        {
          continue;
        }
        if (!_failMoveIndices.TryAdd(index, failure ?? new IOException($"Injected failure on Move #{index}.")))
        {
          throw new ArgumentException($"Дублирующийся индекс отказа Move: {index}.", nameof(failMoveIndices));
        }
      }
    }

    public List<(string Source, string Destination)> MoveCalls { get; } = [];

    public List<string> DeleteCalls { get; } = [];

    public bool Exists(string path) => File.Exists(path);

    public void Move(string sourcePath, string destinationPath, bool overwrite)
    {
      MoveCalls.Add((sourcePath, destinationPath));

      int current = _moveCallCount;
      _moveCallCount++;

      if (_failMoveIndices.TryGetValue(current, out var failure))
      {
        throw failure;
      }

      File.Move(sourcePath, destinationPath, overwrite);
    }

    public void Delete(string path)
    {
      DeleteCalls.Add(path);

      int current = _deleteCallCount;
      _deleteCallCount++;

      if (current == _failDeleteIndex)
      {
        throw new IOException($"Injected failure on Delete #{current}.");
      }

      File.Delete(path);
    }
  }
}