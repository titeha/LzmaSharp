using System.Buffers.Binary;
using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// RED-тесты SEC-001 (транзакционное извлечение ZIP, stream-маршрут).
/// Фиксируют целевой инвариант: при отказе операции извлечения с <c>overwrite: true</c>
/// прежние байты существующего файла обязаны остаться нетронутыми, временных/backup-файлов не остаётся.
/// На текущем коде (без staged-записи) оба теста ПАДАЮТ: файл обрезается до проверки пароля/HMAC/CRC
/// и затем удаляется rollback-ом.
/// </summary>
public sealed class ZipStreamExtractorTransactionalTests
{
  /// <summary>Создаёт <see cref="ZipStreamingEntry"/> для потокового writer-а.</summary>
  private static ZipStreamingEntry F(string name, byte[] c)
      => new(name, c.LongLength, () => new MemoryStream(c, writable: false));

  /// <summary>Уникальный временный каталог для изоляции теста.</summary>
  private static string TempDir()
      => Path.Combine(Path.GetTempPath(), "lzs-txex-" + Guid.NewGuid().ToString("N"));

  /// <summary>Детерминированно заполненный массив (seed → NextBytes).</summary>
  private static byte[] MakeFilled(int seed, int size)
  {
    var rnd = new Random(seed);
    byte[] buf = new byte[size];
    rnd.NextBytes(buf);
    return buf;
  }

  /// <summary>
  /// TX-EX-01: извлечение с <c>overwrite: true</c> и неверным паролем.
  /// Ожидается <see cref="ZipExtractResult.WrongPassword"/>; прежние байты sentinel-файла
  /// обязаны уцелеть, остатков <c>*.tmp</c>/<c>*.bak</c> быть не должно.
  /// На текущем коде файл обрезается и удаляется rollback-ом — тест ПАДАЕТ (RED).
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_НеверныйПароль_ПрежниеБайтыСохранены_ИНетОстатков()
  {
    // 1. Данные члена и пароль.
    const int payloadSize = 4096;
    byte[] payload = MakeFilled(seed: 11, payloadSize);
    byte[] pwd = Encoding.UTF8.GetBytes("right-password");

    // 2. Записать зашифрованный архив в seekable MemoryStream.
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(
        [F("aes.bin", payload)], ms,
        progress: null, token: default, currentFile: null,
        maxDegreeOfParallelism: 0, password: pwd));
    byte[] archive = ms.ToArray();

    // 3. Sentinel — заранее подготовленный файл в назначении (другой seed).
    byte[] sentinel = MakeFilled(seed: 7, payloadSize);
    Assert.NotEqual(payload, sentinel); // sentinel обязан отличаться от payload

    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string target = Path.Combine(dest, "aes.bin");
      File.WriteAllBytes(target, sentinel);
      Assert.True(File.Exists(target));

      // 4. Прочитать каталог из seekable-потока.
      using var read = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok,
          ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
      Assert.Single(entries);

      // 5. Извлечение с НЕВЕРНЫМ паролем и overwrite: true.
      var result = ZipStreamExtractor.ExtractToDirectory(
          read, entries, dest, overwrite: true,
          currentFile: null, token: default, progress: null,
          password: Encoding.UTF8.GetBytes("wrong-password"));

      // 6. Assertions — ДО cleanup.
      Assert.Equal(ZipExtractResult.WrongPassword, result);
      Assert.True(File.Exists(target), "прежний файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinel, File.ReadAllBytes(target));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, true);
    }
  }

  /// <summary>
  /// TX-EX-02: извлечение с <c>overwrite: true</c>, верным паролем, но повреждённым телом члена.
  /// Ожидается результат ≠ <see cref="ZipExtractResult.Ok"/>; прежние байты sentinel-файла
  /// обязаны уцелеть, остатков <c>*.tmp</c>/<c>*.bak</c> быть не должно.
  /// На текущем коде файл обрезается и удаляется rollback-ом — тест ПАДАЕТ (RED).
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_ПовреждённыеДанные_ПрежниеБайтыСохранены_ИНетОстатков()
  {
    // 1. Данные члена и пароль.
    const int payloadSize = 4096;
    byte[] payload = MakeFilled(seed: 11, payloadSize);
    byte[] pwd = Encoding.UTF8.GetBytes("right-password");

    // 2. Записать зашифрованный архив.
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(
        [F("aes.bin", payload)], ms,
        progress: null, token: default, currentFile: null,
        maxDegreeOfParallelism: 0, password: pwd));
    byte[] archive = ms.ToArray();

    // 3. Прочитать каталог, чтобы получить метаданные.
    using (var readMeta = new MemoryStream(archive, writable: false))
    {
      Assert.Equal(ZipReadResult.Ok,
          ZipStreamReader.ReadCentralDirectory(readMeta, out ZipStreamEntry[] metaEntries));
      Assert.Single(metaEntries);

      // 4. Вычислить dataStart ТОЧНО как в production (ZipStreamExtractor.cs:88–90).
      var e = metaEntries[0];
      int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(
          archive.AsSpan((int)e.LocalHeaderOffset + 26, 2));
      int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(
          archive.AsSpan((int)e.LocalHeaderOffset + 28, 2));
      long dataStart = e.LocalHeaderOffset + 30 + nameLen + extraLen;

      // 5. Инъекция повреждения: инверсия 8 байт в середине тела члена.
      long mid = dataStart + Math.Max(1, e.CompressedSize / 2);
      Assert.True(mid >= dataStart && mid < dataStart + e.CompressedSize,
          "повреждение не попало в тело члена");
      for (int k = 0; k < 8 && mid + k < archive.LongLength; k++)
        archive[mid + k] ^= 0xFF;
    }

    // 6. Sentinel — заранее подготовленный файл в назначении.
    byte[] sentinel = MakeFilled(seed: 7, payloadSize);
    Assert.NotEqual(payload, sentinel);

    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string target = Path.Combine(dest, "aes.bin");
      File.WriteAllBytes(target, sentinel);
      Assert.True(File.Exists(target));

      // 7. Прочитать каталог из повреждённого архива (seekable).
      using var read = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok,
          ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
      Assert.Single(entries);

      // 8. Извлечение с ВЕРНЫМ паролем и overwrite: true.
      var result = ZipStreamExtractor.ExtractToDirectory(
          read, entries, dest, overwrite: true,
          currentFile: null, token: default, progress: null,
          password: pwd);

      // 9. Assertions — ДО cleanup.
      Assert.NotEqual(ZipExtractResult.Ok, result);
      Assert.True(File.Exists(target), "прежний файл удалён — потеря данных (SEC-001)");
      Assert.Equal(sentinel, File.ReadAllBytes(target));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, true);
    }
  }

  /// <summary>
  /// Регрессия на находку кросс-семейного ревью (BLOCKING 1): успешная перезапись файла
  /// с атрибутом <see cref="FileAttributes.ReadOnly"/> не должна оставлять backup в каталоге
  /// назначения. До исправления <c>CleanupAfterCommit</c> не мог удалить <c>.bak</c>
  /// (атрибут ReadOnly переносится вместе с файлом при <c>File.Move</c>), и операция
  /// возвращала <c>Ok</c> с мусором в каталоге пользователя.
  /// Только Windows: атрибут ReadOnly блокирует удаление именно там.
  /// </summary>
  [Fact]
  public void Извлечь_Перезапись_Успех_ПрежнийReadOnly_НеОставляетBackup()
  {
    if (!OperatingSystem.IsWindows())
      Assert.Skip("Атрибут ReadOnly блокирует удаление файла только на Windows; на POSIX успех определяется правами каталога.");

    const int payloadSize = 4096;
    byte[] payload = MakeFilled(seed: 21, payloadSize);
    byte[] sentinel = MakeFilled(seed: 22, payloadSize);
    Assert.NotEqual(payload, sentinel);

    // Архив без шифрования: проверяется успешный путь публикации и последующей очистки.
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(
        [F("keep.bin", payload)], ms,
        progress: null, token: default, currentFile: null,
        maxDegreeOfParallelism: 0, password: null));
    byte[] archive = ms.ToArray();

    string dest = TempDir();
    try
    {
      Directory.CreateDirectory(dest);
      string target = Path.Combine(dest, "keep.bin");
      File.WriteAllBytes(target, sentinel);
      File.SetAttributes(target, FileAttributes.ReadOnly);
      Assert.True(File.Exists(target));

      using var read = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok,
          ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
      Assert.Single(entries);

      var result = ZipStreamExtractor.ExtractToDirectory(
          read, entries, dest, overwrite: true,
          currentFile: null, token: default, progress: null,
          password: null);

      // Успех, новое содержимое опубликовано, мусора в каталоге назначения нет.
      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(payload, File.ReadAllBytes(target));
      Assert.Empty(Directory.GetFiles(dest, "*.bak", SearchOption.AllDirectories));
      Assert.Empty(Directory.GetFiles(dest, "*.tmp", SearchOption.AllDirectories));
      Assert.Single(Directory.GetFiles(dest));
    }
    finally
    {
      if (Directory.Exists(dest))
      {
        // ReadOnly снимается, иначе Directory.Delete откажет (в т.ч. на остатке .bak, если фикс сломается).
        foreach (string f in Directory.GetFiles(dest, "*", SearchOption.AllDirectories))
        {
          try { File.SetAttributes(f, FileAttributes.Normal); }
          catch (IOException) { }
          catch (UnauthorizedAccessException) { }
        }

        Directory.Delete(dest, true);
      }
    }
  }
}
