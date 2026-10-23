using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Characterization-тесты отката для ZIP in-memory маршрута (TX-EX-03).
/// <para>
/// <c>ZipExtractor.ExtractCore</c> — общее ядро для обоих ZIP-маршрутов (in-memory и потокового).
/// После шага B3 оно переведено на staged-запись, поэтому откат при отказе на N-м элементе
/// обязан восстановить прежние байты опубликованных файлов и удалить созданные новые.
/// Эти тесты подтверждают инвариант characterization-стилем: они ожидаются ЗЕЛЁНЫМИ сразу,
/// потому что фикс уже в общем ядре. Если какой-то из них упадёт — это реальная находка.
/// </para>
/// </summary>
public sealed class ZipExtractorRollbackTests
{
  /// <summary>Создаёт уникальную временную папку (не создавая её на диске).</summary>
  private static string NewTempDir()
    => Path.Combine(Path.GetTempPath(), "lzs-rollback-" + Guid.NewGuid().ToString("N"));

  /// <summary>Детерминированно заполненный массив (seed → NextBytes).</summary>
  private static byte[] MakeFilled(int seed, int size)
  {
    var rnd = new Random(seed);
    byte[] buf = new byte[size];
    rnd.NextBytes(buf);
    return buf;
  }

  /// <summary>
  /// TX-EX-03: отказ на втором элементе (исключение <see cref="IOException"/>).
  /// <para>
  /// Два элемента с sentinel-файлами в назначении. Делегат <c>writeAt</c> для i==0 пишет
  /// в staged-путь и возвращает <see cref="ZipExtractResult.Ok"/>, для i==1 бросает
  /// <see cref="IOException"/>. Ожидается <see cref="ZipExtractResult.IOError"/>, оба sentinel
  /// восстановлены байт-в-байт, остатков <c>*.tmp</c>/<c>*.bak</c> нет.
  /// </para>
  /// </summary>
  [Fact]
  public void ExtractCore_ОтказНаВторомЭлементе_ПрежниеБайтыОбоихФайловВосстановлены()
  {
    // Данные элементов (отличаются от sentinel).
    byte[] d0 = MakeFilled(seed: 10, 128);
    byte[] d1 = MakeFilled(seed: 11, 128);

    // Sentinel-файлы в назначении.
    byte[] s0 = MakeFilled(seed: 20, 128);
    byte[] s1 = MakeFilled(seed: 21, 128);
    Assert.NotEqual(d0, s0);
    Assert.NotEqual(d1, s1);

    string dest = NewTempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string f0 = Path.Combine(dest, "file0.bin");
      string f1 = Path.Combine(dest, "file1.bin");
      File.WriteAllBytes(f0, s0);
      File.WriteAllBytes(f1, s1);

      // Делегат: i==0 пишет, i==1 бросает IOException.
      ZipExtractResult result = ZipExtractor.ExtractCore(
          count: 2,
          nameAt: i => i == 0 ? "file0.bin" : "file1.bin",
          isDirAt: _ => false,
          writeAt: (i, stagedPath) =>
          {
            if (i == 0)
            {
              File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(stagedPath), d0);
              return ZipExtractResult.Ok;
            }
            throw new IOException("имитация отказа записи");
          },
          destinationDirectory: dest,
          overwrite: true,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      // Assertions ДО cleanup.
      Assert.Equal(ZipExtractResult.IOError, result);
      Assert.True(File.Exists(f0), "file0.bin должен существовать (sentinel восстановлен)");
      Assert.Equal(s0, File.ReadAllBytes(f0));
      Assert.True(File.Exists(f1), "file1.bin должен существовать (sentinel восстановлен)");
      Assert.Equal(s1, File.ReadAllBytes(f1));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Отрицательный результат делегата (без исключения) на втором элементе — покрытие ветви
  /// «written != Ok» в <c>ExtractCore</c> отдельно от ветви «исключение».
  /// Ожидается <see cref="ZipExtractResult.InvalidData"/>, оба sentinel восстановлены,
  /// остатков <c>*.tmp</c>/<c>*.bak</c> нет.
  /// </summary>
  [Fact]
  public void ExtractCore_ОтрицательныйРезультатНаВторомЭлементе_ОткатываетПервый()
  {
    byte[] d0 = MakeFilled(seed: 10, 128);
    byte[] d1 = MakeFilled(seed: 11, 128);

    byte[] s0 = MakeFilled(seed: 20, 128);
    byte[] s1 = MakeFilled(seed: 21, 128);
    Assert.NotEqual(d0, s0);
    Assert.NotEqual(d1, s1);

    string dest = NewTempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string f0 = Path.Combine(dest, "file0.bin");
      string f1 = Path.Combine(dest, "file1.bin");
      File.WriteAllBytes(f0, s0);
      File.WriteAllBytes(f1, s1);

      ZipExtractResult result = ZipExtractor.ExtractCore(
          count: 2,
          nameAt: i => i == 0 ? "file0.bin" : "file1.bin",
          isDirAt: _ => false,
          writeAt: (i, stagedPath) =>
          {
            if (i == 0)
            {
              File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(stagedPath), d0);
              return ZipExtractResult.Ok;
            }
            return ZipExtractResult.InvalidData;
          },
          destinationDirectory: dest,
          overwrite: true,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.True(File.Exists(f0), "file0.bin должен существовать (sentinel восстановлен)");
      Assert.Equal(s0, File.ReadAllBytes(f0));
      Assert.True(File.Exists(f1), "file1.bin должен существовать (sentinel восстановлен)");
      Assert.Equal(s1, File.ReadAllBytes(f1));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  /// <summary>
  /// Откат удаляет опубликованные <c>CreatedNew</c>-файлы (которых не было до операции),
  /// но не трогает файлы без backup (существующие, но не входящие в архив).
  /// <para>
  /// Сценарий: в назначении только <c>keep.bin</c> (sentinel, отсутствует в архиве).
  /// Архив: <c>new0.bin</c> и <c>new1.bin</c>. Делегат пишет <c>new0.bin</c> успешно,
  /// на <c>new1.bin</c> бросает <see cref="IOException"/>.
  /// Ожидается <see cref="ZipExtractResult.IOError"/>; <c>new0.bin</c> НЕ существует
  /// (опубликованный CreatedNew удалён откатом); <c>keep.bin</c> существует и байты совпадают
  /// (файл без backup не удаляется никогда); остатков <c>*.tmp</c>/<c>*.bak</c> нет.
  /// </para>
  /// </summary>
  [Fact]
  public void ExtractCore_НовыеФайлы_ПриОткатеУдаляются_ИПрежниеНеТронуты()
  {
    byte[] d0 = MakeFilled(seed: 10, 128);
    byte[] d1 = MakeFilled(seed: 11, 128);
    byte[] sentinel = MakeFilled(seed: 30, 128);

    string dest = NewTempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string keepPath = Path.Combine(dest, "keep.bin");
      File.WriteAllBytes(keepPath, sentinel);

      ZipExtractResult result = ZipExtractor.ExtractCore(
          count: 2,
          nameAt: i => i == 0 ? "new0.bin" : "new1.bin",
          isDirAt: _ => false,
          writeAt: (i, stagedPath) =>
          {
            if (i == 0)
            {
              File.WriteAllBytes(StagedExtractionJournal.ToFileSystemPath(stagedPath), d0);
              return ZipExtractResult.Ok;
            }
            throw new IOException("имитация отказа записи");
          },
          destinationDirectory: dest,
          overwrite: true,
          currentFile: null,
          token: CancellationToken.None,
          sizeAt: null);

      string new0Path = Path.Combine(dest, "new0.bin");
      string new1Path = Path.Combine(dest, "new1.bin");

      Assert.Equal(ZipExtractResult.IOError, result);
      Assert.False(File.Exists(new0Path), "new0.bin НЕ должен существовать (CreatedNew удалён откатом)");
      Assert.False(File.Exists(new1Path), "new1.bin НЕ должен существовать (запись не удалась)");
      Assert.True(File.Exists(keepPath), "keep.bin должен существовать (файл без backup не удаляется)");
      Assert.Equal(sentinel, File.ReadAllBytes(keepPath));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }
}
