using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002 (§4.4 шаг 6): успешное создание архива в файл через staged-запись —
/// проверка публикации (commit) результата.
/// </summary>
public sealed class LzmaArchiveServiceCreateToFileTests
{
  /// <summary>
  /// Проверяет commit нового файла (шаг 5): при успешном создании архив
  /// публикуется по назначению, в каталоге не остаётся staged-файлов,
  /// опубликованный архив открывается и содержимое совпадает байт-в-байт.
  /// </summary>
  [Fact]
  public async Task CreateToFile_Success_PublishesArchiveAndLeavesNoTempFiles()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string destination = Path.Combine(dir, "created.7z");
      byte[] content = Encoding.UTF8.GetBytes("содержимое для round-trip");

      SevenZipStreamingEntry[] entries =
      [
          new SevenZipStreamingEntry("data.txt", Length: content.Length, OpenRead: () => new MemoryStream(content)),
      ];

      SevenZipArchiveWriteResult result = await service.CreateArchiveToFileAsync(
          entries, destination, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16);

      // Операция успешна...
      Assert.Equal(SevenZipArchiveWriteResult.Ok, result);

      // ...архив опубликован по назначению...
      Assert.True(File.Exists(destination));

      // ...и staged-файлов в каталоге не осталось.
      Assert.Equal([destination], Directory.GetFiles(dir));

      // Round-trip: опубликованный архив открывается, содержимое совпадает.
      ArchiveOpenOutcome opened = await service.OpenAsync(File.ReadAllBytes(destination), password: null);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);

      SevenZipDecodedEntry entry = opened.Entries.Single(e => e.Name.Replace('\\', '/') == "data.txt");
      Assert.Equal(content, entry.Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }

  /// <summary>
  /// Проверяет replacement существующего назначения (шаг 7): успешное создание поверх
  /// существующего архива публикует новый архив, полностью замещая старый,
  /// и staged-файлов в каталоге не остаётся.
  /// </summary>
  [Fact]
  public async Task CreateToFile_SuccessOverExistingArchive_ReplacesOldContent()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string destination = Path.Combine(dir, "existing.7z");

      // Существующий архив со старым содержимым.
      byte[] oldContent = Encoding.UTF8.GetBytes("старое содержимое");
      SevenZipStreamingEntry[] oldEntries =
      [
          new SevenZipStreamingEntry("old.txt", Length: oldContent.Length, OpenRead: () => new MemoryStream(oldContent)),
      ];

      SevenZipArchiveWriteResult first = await service.CreateArchiveToFileAsync(
          oldEntries, destination, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, first);
      byte[] original = File.ReadAllBytes(destination);

      // Успешное создание поверх того же пути с новым содержимым.
      byte[] newContent = Encoding.UTF8.GetBytes("новое содержимое для замены");
      SevenZipStreamingEntry[] newEntries =
      [
          new SevenZipStreamingEntry("new.txt", Length: newContent.Length, OpenRead: () => new MemoryStream(newContent)),
      ];

      SevenZipArchiveWriteResult second = await service.CreateArchiveToFileAsync(
          newEntries, destination, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16);

      Assert.Equal(SevenZipArchiveWriteResult.Ok, second);

      // В каталоге только целевой файл, staged-остатков нет.
      Assert.Equal([destination], Directory.GetFiles(dir));

      // Опубликован именно новый архив: старое содержимое полностью замещено.
      byte[] published = File.ReadAllBytes(destination);
      Assert.NotEqual(original, published);

      ArchiveOpenOutcome opened = await service.OpenAsync(published, password: null);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);

      SevenZipDecodedEntry entry = opened.Entries.Single(e => e.Name.Replace('\\', '/') == "new.txt");
      Assert.Equal(newContent, entry.Bytes);
      Assert.DoesNotContain(opened.Entries, e => e.Name.Replace('\\', '/') == "old.txt");
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }
}
