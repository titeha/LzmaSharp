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
  /// Отказ записи: инъекция платформенно-зависимого отказа.
  /// Windows: FileAttributes.ReadOnly на файле — File.Move(overwrite:true) не может
  /// перезаписать read-only файл → UnauthorizedAccessException.
  /// POSIX: права на файл не блокируют unlink/rename (решают права каталога),
  /// поэтому лишаем прав записи КАТАЛОГ — File.SetUnixFileMode без UserWrite.
  /// Это блокирует создание staged-файла в каталоге → UnauthorizedAccessException.
  /// Инварианты: written == false, байты назначения целы, в каталоге только назначение.
  /// Ограничения POSIX-ветки (честно, покрытие неполное):
  /// 1) отказ возникает ДО публикации (на OpenWrite), поэтому сама фаза публикации
  ///    (<see cref="StagedDestination.Commit"/> → File.Move) на POSIX этим тестом не покрывается;
  ///    для точной инъекции отказа публикации нужен отдельный шов (отслеживается отдельным шагом);
  /// 2) процесс с правами root игнорирует права каталога, поэтому в таком окружении инъекция
  ///    не сработает (CI-раннеры GitHub работают под непривилегированным пользователем).
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

      if (OperatingSystem.IsWindows())
      {
        File.SetAttributes(destination, FileAttributes.ReadOnly);
      }
      else
      {
        // POSIX: отказ через права каталога (не файла).
        // Убираем UserWrite — нельзя создать staged-файл в каталоге.
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
      }

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
        if (OperatingSystem.IsWindows())
        {
          File.SetAttributes(destination, FileAttributes.Normal);
        }
        else
        {
          // Восстанавливаем права каталога для корректной очистки.
          File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
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
