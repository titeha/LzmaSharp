using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002: сохранность существующего архива при неудачном создании нового поверх того же пути.
/// </summary>
public sealed class LzmaArchiveServiceCreatePreservationTests
{
  /// <summary>
  /// SEC-002 (SECURITY_REMEDIATION_PLAN §4.2, блокирующий регрессионный тест): если операция
  /// создания архива завершилась ошибкой, существовавший по тому же пути архив должен остаться
  /// байт-в-байт прежним. Отказ воспроизводится некорректным набором записей, который валидация
  /// отклоняет после открытия выходного потока. Исторически тест был красным: старый путь открывал
  /// назначение с <c>FileMode.Create</c> и обрезал существующий архив до записи первого полезного
  /// байта; после перевода одиночного 7z-пути на staged-запись (шаги 1–3 §4.4) тест зелёный.
  /// </summary>
  [Fact]
  public async Task Create_DestinationWriteFailure_PreservesExistingArchive()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      // Существующий архив с известными байтами.
      string destination = Path.Combine(dir, "existing.7z");
      ArchiveCreateOutcome existing = await service.CreateArchiveAsync(
          [new SevenZipArchiveWriterEntry("data.txt", Encoding.UTF8.GetBytes("исходное содержимое"))],
          SevenZipWriterCompressionMethod.Copy);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, existing.Result);
      File.WriteAllBytes(destination, existing.Archive);
      byte[] original = File.ReadAllBytes(destination);

      // Некорректный набор: у непустого файла отсутствует OpenRead — валидация вернёт
      // InvalidData после того, как выходной поток уже открыт.
      SevenZipStreamingEntry[] entries =
      [
          new SevenZipStreamingEntry("broken.bin", Length: 10, OpenRead: null!),
      ];

      SevenZipArchiveWriteResult result = await service.CreateArchiveToFileAsync(
          entries, destination, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16);

      // Операция обязана завершиться ошибкой...
      Assert.Equal(SevenZipArchiveWriteResult.InvalidData, result);

      // ...а существующий архив должен остаться байт-в-байт прежним.
      Assert.Equal(original, File.ReadAllBytes(destination));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
  }

  /// <summary>
  /// SEC-002 (§4.4 шаг 4): отказ записи в середине операции. Вторая запись бросает
  /// <see cref="IOException"/> при чтении источника, когда байты первой записи уже
  /// записаны в staged-файл (<c>maxDegreeOfParallelism: 1</c> даёт пофайловые волны).
  /// Ожидание: операция завершается ошибкой, существующий архив байт-в-байт прежний,
  /// staged-файлы в каталоге назначения не остаются.
  /// </summary>
  [Fact]
  public async Task Create_SourceReadFailureMidWrite_PreservesArchiveAndCleansStaging()
  {
    var service = new LzmaArchiveService();

    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-sec002-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      // Существующий архив с известными байтами.
      string destination = Path.Combine(dir, "existing.7z");
      ArchiveCreateOutcome existing = await service.CreateArchiveAsync(
          [new SevenZipArchiveWriterEntry("data.txt", Encoding.UTF8.GetBytes("исходное содержимое"))],
          SevenZipWriterCompressionMethod.Copy);
      Assert.Equal(SevenZipArchiveWriteResult.Ok, existing.Result);
      File.WriteAllBytes(destination, existing.Archive);
      byte[] original = File.ReadAllBytes(destination);

      // Первая запись штатно пишется в staging, вторая бросает IOException при чтении.
      SevenZipStreamingEntry[] entries =
      [
          new SevenZipStreamingEntry("good.bin", Length: 64, OpenRead: () => new MemoryStream(new byte[64])),
          new SevenZipStreamingEntry("broken.bin", Length: 64, OpenRead: () => new ThrowAfterReadStream(failAfter: 16)),
      ];

      SevenZipArchiveWriteResult result = await service.CreateArchiveToFileAsync(
          entries, destination, SevenZipWriterCompressionMethod.Copy, dictionarySize: 1 << 16, maxDegreeOfParallelism: 1);

      // Отказ источника в параллельной волне writer преобразует в InternalError.
      Assert.Equal(SevenZipArchiveWriteResult.InternalError, result);

      // Существующий архив байт-в-байт прежний, partial не опубликован.
      Assert.Equal(original, File.ReadAllBytes(destination));

      // Временных staged-файлов в каталоге назначения не осталось.
      Assert.Equal([destination], Directory.GetFiles(dir));
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }
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
