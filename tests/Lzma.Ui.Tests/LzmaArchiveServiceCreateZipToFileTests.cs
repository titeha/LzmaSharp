using System.Text;

using Lzma.Core.Zip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002 (§4.4 шаг 9): создание ZIP-архива в файл через staged-запись —
/// публикация результата при успехе и сохранность существующего архива при отказе.
/// </summary>
public sealed class LzmaArchiveServiceCreateZipToFileTests
{
  /// <summary>
  /// Проверяет успешное создание: ZIP публикуется по назначению,
  /// staged-файлов в каталоге не остаётся, архив листится и извлекается
  /// с совпадением содержимого байт-в-байт.
  /// </summary>
  [Fact]
  public async Task CreateZipToFile_Success_PublishesArchiveAndLeavesNoTempFiles()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "created.zip");
      byte[] content = Encoding.UTF8.GetBytes("zip-содержимое для round-trip");

      ZipStreamingEntry[] entries =
      [
          new ZipStreamingEntry("data.txt", content.Length, () => new MemoryStream(content)),
      ];

      ZipWriteResult result = await service.CreateZipToFileAsync(entries, destination);

      // Операция успешна, архив опубликован, staged-остатков нет.
      Assert.Equal(ZipWriteResult.Ok, result);
      Assert.True(File.Exists(destination));
      Assert.Equal([destination], Directory.GetFiles(dir));

      // Round-trip: опубликованный архив листится и извлекается, содержимое совпадает.
      ZipListOutcome listed = await service.OpenZipFromFileAsync(destination);
      Assert.Equal(ZipReadResult.Ok, listed.Result);
      Assert.Contains(listed.Entries, e => e.Name == "data.txt" && e.UncompressedSize == content.Length);

      string extractDir = Path.Combine(dir, "extract");
      ZipExtractResult extracted = await service.ExtractZipFileAsync(destination, extractDir);
      Assert.Equal(ZipExtractResult.Ok, extracted);
      Assert.Equal(content, File.ReadAllBytes(Path.Combine(extractDir, "data.txt")));
    }
    finally
    {
      Cleanup(dir);
    }
  }

  /// <summary>
  /// Проверяет сохранность при отказе валидации: пустое имя записи отклоняется writer-ом
  /// после открытия выходного потока; существующий ZIP остаётся байт-в-байт прежним,
  /// staged-файлов не остаётся.
  /// </summary>
  [Fact]
  public async Task CreateZipToFile_ValidationFailure_PreservesExistingZipAndCleansStaging()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "existing.zip");
      byte[] original = await CreateExistingZipAsync(service, destination);

      // Некорректный набор: пустое имя записи.
      ZipStreamingEntry[] entries =
      [
          new ZipStreamingEntry("", 10, () => new MemoryStream(new byte[10])),
      ];

      ZipWriteResult result = await service.CreateZipToFileAsync(entries, destination);

      Assert.Equal(ZipWriteResult.InvalidData, result);
      Assert.Equal(original, File.ReadAllBytes(destination));
      Assert.Equal([destination], Directory.GetFiles(dir));
    }
    finally
    {
      Cleanup(dir);
    }
  }

  /// <summary>
  /// Проверяет сохранность при отказе чтения источника: вторая запись бросает
  /// <see cref="IOException"/> при чтении; writer возвращает InvalidData (чтение волной
  /// на всех ядрах может пройти до записи первых байт в staging — контракт соблюдается
  /// в любом случае: публикации нет, staged убирается, назначение не тронуто).
  /// </summary>
  [Fact]
  public async Task CreateZipToFile_SourceReadFailure_PreservesExistingZipAndCleansStaging()
  {
    var service = new LzmaArchiveService();

    string dir = NewDirectory();
    try
    {
      string destination = Path.Combine(dir, "existing.zip");
      byte[] original = await CreateExistingZipAsync(service, destination);

      ZipStreamingEntry[] entries =
      [
          new ZipStreamingEntry("good.bin", 64, () => new MemoryStream(new byte[64])),
          new ZipStreamingEntry("broken.bin", 64, () => new ThrowAfterReadStream(failAfter: 16)),
      ];

      ZipWriteResult result = await service.CreateZipToFileAsync(entries, destination);

      Assert.Equal(ZipWriteResult.InvalidData, result);
      Assert.Equal(original, File.ReadAllBytes(destination));
      Assert.Equal([destination], Directory.GetFiles(dir));
    }
    finally
    {
      Cleanup(dir);
    }
  }

  private static string NewDirectory()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-zip-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }

  private static void Cleanup(string dir)
  {
    try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
  }

  /// <summary>
  /// Создаёт существующий ZIP по пути назначения и возвращает его байты.
  /// </summary>
  private static async Task<byte[]> CreateExistingZipAsync(LzmaArchiveService service, string destination)
  {
    byte[] content = Encoding.UTF8.GetBytes("исходное содержимое");
    ZipStreamingEntry[] entries =
    [
        new ZipStreamingEntry("data.txt", content.Length, () => new MemoryStream(content)),
    ];

    ZipWriteResult result = await service.CreateZipToFileAsync(entries, destination);

    Assert.Equal(ZipWriteResult.Ok, result);
    return File.ReadAllBytes(destination);
  }

  /// <summary>
  /// Тестовый помощник: поток, который после <c>failAfter</c> прочитанных байт бросает
  /// <see cref="IOException"/> — инъекция отказа чтения источника.
  /// </summary>
  private sealed class ThrowAfterReadStream : Stream
  {
    private readonly int _failAfter;
    private int _read;

    public ThrowAfterReadStream(int failAfter)
    {
      _failAfter = failAfter;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
      get => throw new NotSupportedException();
      set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
      if (_read >= _failAfter)
      {
        throw new IOException("Инъекция отказа чтения (тестовый поток).");
      }

      int n = Math.Min(count, _failAfter - _read);
      Array.Clear(buffer, offset, n);
      _read += n;
      return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
