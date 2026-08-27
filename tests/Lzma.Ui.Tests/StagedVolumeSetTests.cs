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
  /// Fake файловых операций: по умолчанию делегирует <see cref="File"/>, детерминированно
  /// считает вызовы Move и выбрасывает IOException на точно заданном номере Move (нумерация
  /// с нуля). Исключение возникает только в Move, не в Exists/Delete.
  /// </summary>
  private sealed class StagedFileOperationsFake : IStagedVolumeFileOperations
  {
    private readonly int _failMoveIndex;
    private int _moveCallCount;

    public StagedFileOperationsFake(int failMoveIndex)
    {
      _failMoveIndex = failMoveIndex;
    }

    public List<(string Source, string Destination)> MoveCalls { get; } = [];

    public bool Exists(string path) => File.Exists(path);

    public void Move(string sourcePath, string destinationPath, bool overwrite)
    {
      MoveCalls.Add((sourcePath, destinationPath));

      int current = _moveCallCount;
      _moveCallCount++;

      if (current == _failMoveIndex)
      {
        throw new IOException($"Injected failure on Move #{current}.");
      }

      File.Move(sourcePath, destinationPath, overwrite);
    }

    public void Delete(string path) => File.Delete(path);
  }
}