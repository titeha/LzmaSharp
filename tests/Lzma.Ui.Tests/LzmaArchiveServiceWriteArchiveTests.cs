using System.Text;

using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002: запись готового архива из памяти в файл (<see cref="LzmaArchiveService.WriteArchiveAsync"/>)
/// через staged-запись — публикация при успехе и сохранность назначения при отказе.
/// </summary>
public sealed class LzmaArchiveServiceWriteArchiveTests
{
  /// <summary>
  /// Успешная запись в новый путь: байты публикуются один в один,
  /// staged-файлов в каталоге не остаётся.
  /// </summary>
  [Fact]
  public async Task WriteArchive_Success_PublishesBytesAndLeavesNoTempFiles()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "created.7z");
      byte[] archive = Encoding.UTF8.GetBytes("байты архива для записи");

      bool written = await service.WriteArchiveAsync(archive, destination);

      Assert.True(written);
      Assert.Equal(archive, File.ReadAllBytes(destination));
      Assert.Equal([destination], Directory.GetFiles(dir));
    }
    finally
    {
      Cleanup(dir);
    }
  }

  /// <summary>
  /// Успешная запись поверх существующего файла: содержимое полностью замещается,
  /// staged-файлов не остаётся.
  /// </summary>
  [Fact]
  public async Task WriteArchive_SuccessOverExistingFile_ReplacesContent()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "existing.7z");
      byte[] original = Encoding.UTF8.GetBytes("старое содержимое");
      File.WriteAllBytes(destination, original);

      byte[] archive = Encoding.UTF8.GetBytes("новое содержимое архива");

      bool written = await service.WriteArchiveAsync(archive, destination);

      Assert.True(written);
      Assert.Equal(archive, File.ReadAllBytes(destination));
      Assert.Equal([destination], Directory.GetFiles(dir));
    }
    finally
    {
      Cleanup(dir);
    }
  }

  /// <summary>
  /// Отказ записи: назначение только для чтения, поэтому публикация (File.Move поверх
  /// read-only файла) падает; существующий архив остаётся байт-в-байт прежним,
  /// staged-файлов не остаётся.
  /// </summary>
  [Fact]
  public async Task WriteArchive_PublishFailure_PreservesExistingArchiveAndCleansStaging()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "existing.7z");
      byte[] original = Encoding.UTF8.GetBytes("исходное содержимое");
      File.WriteAllBytes(destination, original);
      File.SetAttributes(destination, FileAttributes.ReadOnly);
      try
      {
        byte[] archive = Encoding.UTF8.GetBytes("новое содержимое, которое не должно опубликоваться");

        bool written = await service.WriteArchiveAsync(archive, destination);

        Assert.False(written);
        Assert.Equal(original, File.ReadAllBytes(destination));
        Assert.Equal([destination], Directory.GetFiles(dir));
      }
      finally
      {
        File.SetAttributes(destination, FileAttributes.Normal);
      }
    }
    finally
    {
      Cleanup(dir);
    }
  }

  private static string NewDirectory()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-write-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static void Cleanup(string dir)
  {
    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
  }
}
