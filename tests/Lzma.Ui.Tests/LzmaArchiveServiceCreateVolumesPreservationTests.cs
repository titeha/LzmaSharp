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
  /// SEC-002 (§4.4 шаги 10.4–10.5): успешное создание многотомного архива поверх
  /// существующего набора БОЛЬШЕГО размера: новые тома публикуются, лишние старые
  /// тома удаляются, опубликованный архив извлекается с совпадением содержимого.
  /// </summary>
  [Fact]
  public async Task CreateVolumes_SuccessOverLargerOldSet_PublishesAndRemovesStaleVolumes()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-vol-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string basePath = Path.Combine(dir, "existing.7z");

      // Исходный многотомный набор: содержимое больше одного тома.
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
      Assert.True(Directory.GetFiles(dir).Length >= 2);

      // Успешное создание поверх той же базы меньшим архивом (один том).
      byte[] newContent = Encoding.UTF8.GetBytes("маленький архив на один том");

      SevenZipStreamingEntry[] newEntries =
      [
          new SevenZipStreamingEntry("new.txt", newContent.Length, () => new MemoryStream(newContent)),
      ];

      SevenZipArchiveWriteResult second = await service.CreateArchiveToFileAsync(
          newEntries, basePath, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, volumeSize: 1024);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, second);

      // Опубликован только том .001; лишние старые тома удалены.
      string firstVolume = basePath + ".001";
      Assert.Equal([firstVolume], Directory.GetFiles(dir));

      // Round-trip через первый том: содержимое совпадает байт-в-байт.
      string extractDir = Path.Combine(dir, "extract");
      SevenZipArchiveDecodeResult decoded = await service.ExtractArchiveFileAsync(firstVolume, extractDir);
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, decoded);
      Assert.Equal(newContent, File.ReadAllBytes(Path.Combine(extractDir, "new.txt")));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }
}
