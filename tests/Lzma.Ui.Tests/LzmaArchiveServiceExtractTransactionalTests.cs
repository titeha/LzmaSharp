using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// B8 (SEC-001): интеграционные проверки маршрутов извлечения СЕРВИСА <see cref="LzmaArchiveService"/>
/// из файла. Оба потоковых маршрута сервиса идут через общие staged-ядра
/// (<c>ZipStreamExtractor.ExtractToDirectory</c> → <c>ZipExtractor.ExtractCore</c> и
/// <c>SevenZipArchiveDecoder.ExtractToDirectoryFromStream</c> → <c>ExtractHeaderToDirectory</c>),
/// поэтому любой отказ до commit не оставляет опубликованных элементов, файлов стадии (<c>.tmp</c>)
/// и backup (<c>.bak</c>), а прежние байты в папке назначения сохранены. Таблица call graph —
/// в отчёте шага B8.
/// </summary>
public sealed class LzmaArchiveServiceExtractTransactionalTests
{
  private static string NewTempDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzs-b8-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static void TryDeleteDir(string dir)
  {
    try
    {
      if (Directory.Exists(dir))
        Directory.Delete(dir, recursive: true);
    }
    catch (IOException)
    {
      // best-effort
    }
    catch (UnauthorizedAccessException)
    {
      // best-effort
    }
  }

  /// <summary>Несжимаемые данные: <c>ZipWriter</c> выбирает для них Store, поэтому payload
  /// присутствует в архиве дословно и его можно испортить по известному смещению.</summary>
  private static byte[] Incompressible(int size, int seed)
  {
    byte[] data = new byte[size];
    new Random(seed).NextBytes(data);
    return data;
  }

  /// <summary>Файлы стадии (<c>.tmp</c>) и backup (<c>.bak</c>) под папкой назначения: после отказа
  /// их не должно оставаться (откат журнала удаляет и то, и другое).</summary>
  private static string[] JournalArtifacts(string dir)
      => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
          .Where(p => p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                   || p.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
          .ToArray();

  /// <summary>
  /// Потоковый ZIP из файла: данные ВТОРОГО члена испорчены (CRC не сходится). К моменту отказа
  /// первый член уже опубликован (<c>ExtractCore</c> публикует каждый элемент сразу после записи),
  /// поэтому отказ обязан снять всю операцию: первого члена нет, прежние байты соседнего файла целы,
  /// файлов стадии и backup не осталось.
  /// </summary>
  [Fact]
  public async Task ExtractZipFile_ПорчаВторогоЧлена_ПервыйСнятОткатом_ПрежниеБайтыИНетВременныхФайлов()
  {
    byte[] first = Incompressible(4096, seed: 1);
    byte[] second = Incompressible(4096, seed: 2);

    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
        [new ZipWriterEntry("a.bin", first), new ZipWriterEntry("b.bin", second)], out byte[] archive));

    int offset = archive.AsSpan().IndexOf(second);
    Assert.True(offset >= 0,
        "payload второго члена не найден в архиве: ZipWriter выбрал не Store — тест нужно перестроить");
    archive[offset + second.Length / 2] ^= 0xFF; // порча данных без изменения размеров

    string zipPath = Path.Combine(Path.GetTempPath(), "lzs-b8-" + Guid.NewGuid().ToString("N") + ".zip");
    File.WriteAllBytes(zipPath, archive);

    string dest = NewTempDir();
    byte[] previous = Encoding.UTF8.GetBytes("прежние байты");
    File.WriteAllBytes(Path.Combine(dest, "sentinel.txt"), previous);

    var reported = new List<string>();

    try
    {
      ZipExtractResult result = await new LzmaArchiveService().ExtractZipFileAsync(
          zipPath, dest, CancellationToken.None, new DelegateProgress<string>(reported.Add));

      Assert.Equal(ZipExtractResult.InvalidData, result);
      // Отказ случился ПОСЛЕ публикации первого члена: отчёт об имени следующего элемента
      // приходит только после Publish предыдущего. Значит снятие a.bin ниже — работа отката,
      // а не «не успели записать».
      Assert.Equal(["a.bin", "b.bin"], reported);
      Assert.Equal(previous, File.ReadAllBytes(Path.Combine(dest, "sentinel.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "a.bin")),
          "первый член был опубликован до отказа второго — откат обязан его снять");
      Assert.False(File.Exists(Path.Combine(dest, "b.bin")));
      Assert.Empty(JournalArtifacts(dest));
    }
    finally
    {
      File.Delete(zipPath);
      TryDeleteDir(dest);
    }
  }

  /// <summary>
  /// Потоковый ZIP из файла: на месте первого члена уже есть файл, а сервис извлекает строго
  /// с <c>overwrite: false</c>. Отказ происходит до первой записи, прежний файл не изменяется
  /// и не уезжает в <c>.bak</c>, второй член не создаётся.
  /// </summary>
  [Fact]
  public async Task ExtractZipFile_КонфликтИмени_ПрежнийФайлНеИзменён_ВременныхФайловНет()
  {
    byte[] content = Encoding.UTF8.GetBytes("новое содержимое из архива");

    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
        [new ZipWriterEntry("a.txt", content), new ZipWriterEntry("b.txt", content)], out byte[] archive));

    string zipPath = Path.Combine(Path.GetTempPath(), "lzs-b8-" + Guid.NewGuid().ToString("N") + ".zip");
    File.WriteAllBytes(zipPath, archive);

    string dest = NewTempDir();
    byte[] previous = Encoding.UTF8.GetBytes("прежние байты a.txt");
    File.WriteAllBytes(Path.Combine(dest, "a.txt"), previous);

    try
    {
      ZipExtractResult result = await new LzmaArchiveService().ExtractZipFileAsync(zipPath, dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Equal(previous, File.ReadAllBytes(Path.Combine(dest, "a.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "b.txt")));
      Assert.Empty(JournalArtifacts(dest));
    }
    finally
    {
      File.Delete(zipPath);
      TryDeleteDir(dest);
    }
  }

  /// <summary>
  /// Потоковый 7z из файла (<c>ExtractArchiveFileAsync</c>): элемент с именем 300 символов
  /// (300 + 21 суффикс стадии &gt; лимит компонента 255). Pre-flight длины пути виден через сервис
  /// как <see cref="SevenZipArchiveDecodeResult.PathTooLong"/> — именно его UI показывает отдельным
  /// сообщением; данные не пишутся, прежние байты и каталог назначения сохранены.
  /// </summary>
  [Fact]
  public async Task ExtractArchiveFile_СлишкомДлинноеИмяЭлемента_PathTooLong_ПрежниеБайтыИНетВременныхФайлов()
  {
    byte[] payload = Encoding.UTF8.GetBytes("data");
    string longName = new string('x', 300) + ".txt";

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(longName, payload)],
        SevenZipWriterCompressionMethod.Lzma2,
        out byte[] archive));

    string archivePath = Path.Combine(Path.GetTempPath(), "lzs-b8-" + Guid.NewGuid().ToString("N") + ".7z");
    File.WriteAllBytes(archivePath, archive);

    string dest = NewTempDir();
    byte[] previous = Encoding.UTF8.GetBytes("прежние байты");
    File.WriteAllBytes(Path.Combine(dest, "sentinel.txt"), previous);

    try
    {
      SevenZipArchiveDecodeResult result = await new LzmaArchiveService().ExtractArchiveFileAsync(archivePath, dest);

      Assert.Equal(SevenZipArchiveDecodeResult.PathTooLong, result);
      Assert.Equal(previous, File.ReadAllBytes(Path.Combine(dest, "sentinel.txt")));

      string[] remaining = Directory.GetFiles(dest);
      Assert.Single(remaining);
      Assert.Equal("sentinel.txt", Path.GetFileName(remaining[0]));
      Assert.Empty(JournalArtifacts(dest));
    }
    finally
    {
      File.Delete(archivePath);
      TryDeleteDir(dest);
    }
  }
}
