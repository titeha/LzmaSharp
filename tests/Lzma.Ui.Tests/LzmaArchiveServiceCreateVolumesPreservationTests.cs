using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002 (§4.4 шаг 10, multi-volume): сохранность существующих томов при
/// неудачном создании многотомного архива поверх того же базового пути.
/// </summary>
public sealed class LzmaArchiveServiceCreateVolumesPreservationTests
{
  /// <summary>
  /// Красный тест multi-volume фазы SEC-002: если операция создания многотомного
  /// архива завершилась ошибкой, существовавшие тома <c>base.001/…</c> должны остаться
  /// байт-в-байт прежними, а лишних файлов в каталоге появиться не должно.
  /// Воспроизведение отказа: некорректный набор записей отклоняется валидацией ПОСЛЕ
  /// создания выходного потока — конструктор <see cref="VolumeSpanningWriteStream"/>
  /// к этому моменту уже открывает первый том с <c>FileMode.Create</c> и обрезает
  /// существующий <c>.001</c>. Тест должен доказанно падать на текущем production-пути.
  /// </summary>
  [Fact]
  public async Task CreateVolumes_Failure_PreservesExistingVolumes()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-vol-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string basePath = Path.Combine(dir, "existing.7z");

      // Исходный многотомный архив: содержимое заведомо больше одного тома.
      byte[] content = new byte[3000];
      for (int i = 0; i < content.Length; i++)
      {
        content[i] = unchecked((byte)(i * 31));
      }

      SevenZipStreamingEntry[] entries =
      [
          new SevenZipStreamingEntry("data.bin", content.Length, () => new MemoryStream(content)),
      ];

      SevenZipArchiveWriteResult first = await service.CreateArchiveToFileAsync(
          entries, basePath, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, volumeSize: 1024);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, first);

      string[] volumes = Directory.GetFiles(dir).OrderBy(p => p).ToArray();
      Assert.True(volumes.Length >= 2); // действительно многотомный набор

      var originals = volumes.ToDictionary(p => p, File.ReadAllBytes);

      // Неудачное создание поверх того же базового пути: некорректный набор записей.
      SevenZipStreamingEntry[] badEntries =
      [
          new SevenZipStreamingEntry("broken.bin", Length: 10, OpenRead: null!),
      ];

      SevenZipArchiveWriteResult second = await service.CreateArchiveToFileAsync(
          badEntries, basePath, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, volumeSize: 1024);

      // Операция обязана завершиться ошибкой...
      Assert.Equal(SevenZipArchiveWriteResult.InvalidData, second);

      // ...существующие тома должны остаться байт-в-байт прежними...
      foreach (string volume in volumes)
      {
        Assert.Equal(originals[volume], File.ReadAllBytes(volume));
      }

      // ...и лишних файлов (partial-томов) в каталоге появиться не должно.
      Assert.Equal(volumes, Directory.GetFiles(dir).OrderBy(p => p).ToArray());
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }

  /// <summary>
  /// SEC-002 (§4.4, SEC002-M5.3; замена прежнего теста
  /// <c>CreateVolumes_SuccessOverLargerOldSet_PublishesAndRemovesStaleVolumes</c>
  /// после утверждения conservative ownership policy): дополнительный нумерованный
  /// файл непосредственно за новым manifest имеет недоказанный ownership, поэтому
  /// операция отклоняется ДО первой мутации назначения. Сервис в текущей границы
  /// ошибок маппит конфликт (производное от <see cref="IOException"/>) в
  /// <see cref="SevenZipArchiveWriteResult.InternalError"/>. Все тома существовавшего
  /// набора остаются байт-в-байт неизменными, staged/backup-остатки отсутствуют,
  /// старый набор остаётся читаемым. Утверждения об атомарности на уровне ФС здесь
  /// не делаются.
  /// </summary>
  [Fact]
  public async Task CreateVolumes_AdditionalNumberedFiles_PreservesExistingSetAndReportsConflict()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-vol-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string basePath = Path.Combine(dir, "existing.7z");

      // Исходный многотомный набор: содержимое заведомо больше одного тома.
      byte[] oldContent = new byte[3000];
      for (int i = 0; i < oldContent.Length; i++)
      {
        oldContent[i] = unchecked((byte)(i * 31));
      }

      SevenZipStreamingEntry[] oldEntries =
      [
          new SevenZipStreamingEntry("old.bin", oldContent.Length, () => new MemoryStream(oldContent)),
      ];

      SevenZipArchiveWriteResult first = await service.CreateArchiveToFileAsync(
          oldEntries, basePath, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, volumeSize: 1024);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, first);

      // Снимок доказательств сохранности ДО второй операции: точные пути томов,
      // точные байты каждого тома и полный отсортированный список файлов каталога.
      string[] oldVolumes = Directory.GetFiles(dir).OrderBy(p => p).ToArray();
      Assert.True(oldVolumes.Length >= 2, "Исходный набор должен быть действительно многотомным.");
      string[] beforeFiles = (string[])oldVolumes.Clone();
      var originalBytes = oldVolumes.ToDictionary(p => p, File.ReadAllBytes);

      // Попытка создать меньший однотомный архив поверх той же базы: непосредственно
      // за новым manifest существует archive.002 с недоказанным ownership → отказ
      // до первой мутации. Никакого исключения наружу сервиса не ожидается.
      byte[] newContent = Encoding.UTF8.GetBytes("маленький архив на один том");

      SevenZipStreamingEntry[] newEntries =
      [
          new SevenZipStreamingEntry("new.txt", newContent.Length, () => new MemoryStream(newContent)),
      ];

      SevenZipArchiveWriteResult second = await service.CreateArchiveToFileAsync(
          newEntries, basePath, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, volumeSize: 1024);
      Assert.Equal(SevenZipArchiveWriteResult.InternalError, second);

      // Точный состав каталога равен снимку до операции: не осталось ни staged-тома,
      // ни .bak, ни частичного нового final, ни дополнительного нумерованного файла,
      // ни иного артефакта. Ничего не отфильтровывается.
      Assert.Equal(beforeFiles, Directory.GetFiles(dir).OrderBy(p => p).ToArray());

      // Каждый исходный том существует и байт-в-байт равен себе прежнему;
      // точное равенство байтов одновременно доказывает, что в томах нет данных
      // попытки новой записи.
      foreach (string volume in oldVolumes)
      {
        Assert.True(File.Exists(volume));
        Assert.Equal(originalBytes[volume], File.ReadAllBytes(volume));
      }

      // Старый набор остаётся читаемым: склейка томов от первого тома,
      // исходная запись совпадает байт-в-байт.
      string extractDir = Path.Combine(dir, "extract");
      SevenZipArchiveDecodeResult decoded = await service.ExtractArchiveFileAsync(oldVolumes[0], extractDir);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, decoded);
      Assert.Equal(oldContent, File.ReadAllBytes(Path.Combine(extractDir, "old.bin")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }
}
