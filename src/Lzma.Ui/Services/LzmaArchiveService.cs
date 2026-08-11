using System.Collections.Generic;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="IArchiveService"/> поверх ядра <c>Lzma.Core</c>.
/// CPU-операции уводятся с UI-потока через <see cref="Task.Run(System.Action)"/>.
/// </summary>
public sealed class LzmaArchiveService : IArchiveService
{
  /// <inheritdoc />
  public Task<ArchiveOpenOutcome> OpenAsync(byte[] bytes, string? password)
  {
    return Task.Run(() => WithOptions(password, options =>
    {
      SevenZipArchiveDecodeResult result =
          SevenZipArchiveDecoder.DecodeToEntries(bytes, options, out SevenZipDecodedEntry[] entries);

      return new ArchiveOpenOutcome(result, entries);
    }));
  }

  /// <inheritdoc />
  public Task<ZipOpenOutcome> OpenZipAsync(byte[] bytes)
  {
    return Task.Run(() =>
    {
      ZipReadResult result = ZipReader.Read(bytes, out ZipEntry[] entries);
      return new ZipOpenOutcome(result, entries);
    });
  }

  /// <inheritdoc />
  public Task<ZipExtractResult> ExtractZipAsync(
      IReadOnlyList<ZipEntry> entries,
      string destination,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null)
  {
    return Task.Run(
        () => ZipExtractor.ExtractToDirectory(entries, destination, overwrite: false, currentFile, token),
        token);
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(
      byte[] bytes,
      string? password,
      string destination,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null)
  {
    return Task.Run(() => WithOptions(password, options =>
        SevenZipArchiveDecoder.ExtractToDirectory(bytes, options, destination, overwrite: false, out _, progress, token, currentFile)), token);
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveDecodeResult> ExtractSelectedAsync(
      byte[] bytes,
      string? password,
      string destination,
      System.Func<string, bool> shouldExtract,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null)
  {
    return Task.Run(() => WithOptions(password, options =>
        SevenZipArchiveDecoder.ExtractToDirectory(bytes, options, destination, overwrite: false, out _, progress, token, currentFile, shouldExtract)), token);
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveDecodeResult> ExtractSelectedArchiveFileAsync(
      string archivePath,
      string destination,
      System.Func<string, bool> shouldExtract,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null,
      string? password = null)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        return WithOptions(password, options => SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, options, destination, overwrite: false, progress, token, currentFile, shouldExtract));
      }
      catch (System.IO.IOException)
      {
        return SevenZipArchiveDecodeResult.InternalError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return SevenZipArchiveDecodeResult.InternalError;
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<ZipExtractResult> ExtractSelectedZipFileAsync(
      string archivePath,
      string destination,
      System.Func<string, bool> shouldExtract,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null,
      System.IProgress<SevenZipProgress>? progress = null,
      string? password = null)
  {
    return Task.Run(() =>
    {
      byte[]? passwordBytes = password is null ? null : System.Text.Encoding.UTF8.GetBytes(password);
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        ZipReadResult read = ZipStreamReader.ReadCentralDirectory(archive, out ZipStreamEntry[] entries);
        if (read != ZipReadResult.Ok)
          return ZipExtractResult.InvalidData;

        // ZIP-члены независимы → извлекаем отфильтрованное подмножество без правки ядра.
        ZipStreamEntry[] subset = System.Linq.Enumerable.ToArray(
            System.Linq.Enumerable.Where(entries, e => shouldExtract(e.Name)));

        return ZipStreamExtractor.ExtractToDirectory(archive, subset, destination, overwrite: false, currentFile, token, progress, passwordBytes);
      }
      catch (System.IO.IOException)
      {
        return ZipExtractResult.IOError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return ZipExtractResult.IOError;
      }
      finally
      {
        if (passwordBytes is not null)
          System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<ArchiveCreateOutcome> CreateArchiveAsync(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      SevenZipWriterCompressionMethod method,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    return Task.Run(() =>
    {
      SevenZipArchiveWriteResult result = SevenZipArchiveWriter.BuildArchive(
          entries, SevenZipCompressionOptions.ForMethod(method), out byte[] archive, progress, token);

      return new ArchiveCreateOutcome(result, archive);
    }, token);
  }

  /// <inheritdoc />
  public Task<bool> WriteArchiveAsync(byte[] archive, string path)
  {
    return Task.Run(() =>
    {
      try
      {
        System.IO.File.WriteAllBytes(path, archive);
        return true;
      }
      catch (System.IO.IOException)
      {
        return false;
      }
      catch (System.UnauthorizedAccessException)
      {
        return false;
      }
    });
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveWriteResult> CreateArchiveToFileAsync(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      string destinationPath,
      SevenZipWriterCompressionMethod method,
      int dictionarySize,
      int maxDegreeOfParallelism = 0,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<SevenZipCompressionFileProgress>? currentFile = null,
      long volumeSize = 0,
      string? password = null)
  {
    return Task.Run(() =>
    {
      // SEC-002: для одного файла (volumeSize <= 0) пишем в staged-файл рядом с назначением,
      // назначение публикуется только через Commit() после Ok. Ни файлы, ни архив в памяти не держим.
      // Тома по-прежнему пишутся прямо — multi-volume остаётся отдельной поздней фазой (§4.4 шаг 10).
      StagedDestination? staged = volumeSize > 0 ? null : new StagedDestination(destinationPath);
      try
      {
        SevenZipArchiveWriteResult result;

        // Выходной поток должен быть закрыт до Commit: открытый файл перенести нельзя.
        using (System.IO.Stream output = volumeSize > 0
            ? new VolumeSpanningWriteStream(destinationPath, volumeSize)
            : staged!.OpenWrite())
        {
          // Диспетчер по методу: LZMA2/Auto — многопоточно; PPMd/Copy — пофайлово (PPMd последователен).
          result = method switch
          {
            SevenZipWriterCompressionMethod.Lzma2 =>
                SevenZipArchiveWriter.BuildLzma2ArchiveToStream(
                    entries, output, dictionarySize, maxDegreeOfParallelism, progress, token, currentFile),
            SevenZipWriterCompressionMethod.Auto =>
                SevenZipArchiveWriter.BuildAutoSolidArchiveToStream(entries, output, dictionarySize, maxDegreeOfParallelism, progress, token, currentFile),
            SevenZipWriterCompressionMethod.Bcj2 =>
                SevenZipArchiveWriter.BuildBcj2ArchiveToStream(entries, output, progress, token, currentFile, maxDegreeOfParallelism),
            SevenZipWriterCompressionMethod.Aes =>
                BuildAesToStream(entries, output, password, dictionarySize, progress, token, currentFile),
            SevenZipWriterCompressionMethod.Ppmd =>
                SevenZipArchiveWriter.BuildPpmdArchiveToStream(entries, output, progress, token, currentFile, maxDegreeOfParallelism),
            SevenZipWriterCompressionMethod.Copy =>
                SevenZipArchiveWriter.BuildCopyArchiveToStream(entries, output, progress, token, currentFile, maxDegreeOfParallelism),
            _ => SevenZipArchiveWriteResult.NotSupported,
          };
        }

        // Публикуем результат только после полной записи архива и закрытия потока.
        if (result == SevenZipArchiveWriteResult.Ok && staged is not null)
        {
          staged.Commit();
        }

        return result;
      }
      catch (System.IO.IOException)
      {
        return SevenZipArchiveWriteResult.InternalError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return SevenZipArchiveWriteResult.InternalError;
      }
      finally
      {
        // При неудаче (или Ok без публикации) убираем staged-файл без остатка.
        staged?.Dispose();
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveDecodeResult> ExtractArchiveFileAsync(
      string archivePath,
      string destination,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null,
      string? password = null)
  {
    return Task.Run(() =>
    {
      try
      {
        // Читаем архив прямо из файла потоком — в память его не грузим (поддержка > 2 ГиБ).
        // Если путь — первый том (.001), склеиваем тома на лету через VolumeSpanningReadStream.
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        return WithOptions(password, options => SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, options, destination, overwrite: false, progress, token, currentFile));
      }
      catch (System.IO.IOException)
      {
        return SevenZipArchiveDecodeResult.InternalError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return SevenZipArchiveDecodeResult.InternalError;
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<ArchiveListOutcome> OpenFromFileAsync(string archivePath)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        SevenZipArchiveDecodeResult result =
            SevenZipArchiveDecoder.ListEntriesFromStream(archive, out SevenZipListedEntry[] entries);

        return new ArchiveListOutcome(result, entries);
      }
      catch (System.IO.IOException)
      {
        return new ArchiveListOutcome(SevenZipArchiveDecodeResult.InternalError, []);
      }
      catch (System.UnauthorizedAccessException)
      {
        return new ArchiveListOutcome(SevenZipArchiveDecodeResult.InternalError, []);
      }
    });
  }

  /// <inheritdoc />
  public Task<bool> IsZipFileAsync(string archivePath)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        System.Span<byte> head = stackalloc byte[4];
        int read = archive.Read(head);
        // Локальный заголовок 'PK\x03\x04', либо пустой архив 'PK\x05\x06'.
        return read == 4 && head[0] == 0x50 && head[1] == 0x4B
            && (head[2] == 0x03 || head[2] == 0x05 || head[2] == 0x07);
      }
      catch (System.IO.IOException)
      {
        return false;
      }
      catch (System.UnauthorizedAccessException)
      {
        return false;
      }
    });
  }

  /// <inheritdoc />
  public Task<byte[]?> ReadFileBytesAsync(string path)
  {
    return Task.Run<byte[]?>(() =>
    {
      try
      {
        // Файл > 2 ГиБ в один byte[] не поместится — сигнализируем null (открывать надо потоково).
        if (new System.IO.FileInfo(path).Length > int.MaxValue)
          return null;

        return System.IO.File.ReadAllBytes(path);
      }
      catch (System.IO.IOException)
      {
        return null;
      }
      catch (System.UnauthorizedAccessException)
      {
        return null;
      }
    });
  }

  /// <inheritdoc />
  public Task<ZipWriteResult> CreateZipToFileAsync(
      IReadOnlyList<ZipStreamingEntry> entries,
      string destinationPath,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null,
      string? password = null)
  {
    return Task.Run(() =>
    {
      // Пароль (если задан) → UTF-8 байты (WinZip-AES, совместимо с 7-Zip).
      byte[]? passwordBytes = password is null ? null : System.Text.Encoding.UTF8.GetBytes(password);
      // SEC-002 шаг 9: пишем в staged-файл рядом с назначением, ни файлы, ни архив в памяти не держим;
      // назначение публикуется только через Commit() после Ok.
      var staged = new StagedDestination(destinationPath);
      try
      {
        ZipWriteResult result;

        // Выходной поток должен быть закрыт до Commit: открытый файл перенести нельзя.
        using (System.IO.Stream output = staged.OpenWrite())
        {
          result = ZipStreamWriter.Write(entries, output, progress, token, currentFile, maxDegreeOfParallelism: 0, passwordBytes);
        }

        // Публикуем результат только после полной записи архива.
        if (result == ZipWriteResult.Ok)
        {
          staged.Commit();
        }

        return result;
      }
      catch (System.IO.IOException)
      {
        return ZipWriteResult.InvalidData;
      }
      catch (System.UnauthorizedAccessException)
      {
        return ZipWriteResult.InvalidData;
      }
      finally
      {
        // При неудаче (или Ok без публикации) убираем staged-файл без остатка.
        staged.Dispose();
        if (passwordBytes is not null)
          System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<ZipListOutcome> OpenZipFromFileAsync(string archivePath)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        ZipReadResult result = ZipStreamReader.ReadCentralDirectory(archive, out ZipStreamEntry[] entries);
        return new ZipListOutcome(result, entries);
      }
      catch (System.IO.IOException)
      {
        return new ZipListOutcome(ZipReadResult.InvalidData, []);
      }
      catch (System.UnauthorizedAccessException)
      {
        return new ZipListOutcome(ZipReadResult.InvalidData, []);
      }
    });
  }

  /// <inheritdoc />
  public Task<ZipExtractResult> ExtractZipFileAsync(
      string archivePath,
      string destination,
      System.Threading.CancellationToken token = default,
      System.IProgress<string>? currentFile = null,
      System.IProgress<SevenZipProgress>? progress = null,
      string? password = null)
  {
    return Task.Run(() =>
    {
      // Пароль (если задан) → UTF-8 байты (совместимо с 7-Zip WinZip-AES).
      byte[]? passwordBytes = password is null ? null : System.Text.Encoding.UTF8.GetBytes(password);
      try
      {
        // Читаем архив прямо из файла потоком — в память его не грузим (поддержка > 2 ГиБ / ZIP64).
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);

        ZipReadResult read = ZipStreamReader.ReadCentralDirectory(archive, out ZipStreamEntry[] entries);
        if (read != ZipReadResult.Ok)
          return ZipExtractResult.InvalidData; // повреждён / шифрование / неизвестный метод

        return ZipStreamExtractor.ExtractToDirectory(archive, entries, destination, overwrite: false, currentFile, token, progress, passwordBytes);
      }
      catch (System.IO.IOException)
      {
        return ZipExtractResult.IOError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return ZipExtractResult.IOError;
      }
      finally
      {
        if (passwordBytes is not null)
          System.Security.Cryptography.CryptographicOperations.ZeroMemory(passwordBytes);
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<bool> IsZipEncryptedAsync(string archivePath)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);
        if (ZipStreamReader.ReadCentralDirectory(archive, out ZipStreamEntry[] entries) != ZipReadResult.Ok)
          return false;

        foreach (ZipStreamEntry e in entries)
          if (e.IsEncrypted)
            return true;

        return false;
      }
      catch (System.IO.IOException)
      {
        return false;
      }
      catch (System.UnauthorizedAccessException)
      {
        return false;
      }
    });
  }

  /// <inheritdoc />
  public Task<string> DescribeMethodsAsync(byte[] bytes, string? password)
  {
    return Task.Run(() =>
    {
      SevenZipArchiveInspector.TryDescribeMethods(bytes, password, out string description);
      return description ?? string.Empty;
    });
  }

  /// <inheritdoc />
  public Task<bool> IsArchiveEncryptedAsync(string archivePath)
  {
    return Task.Run(() =>
    {
      try
      {
        using System.IO.Stream archive = OpenArchiveReadStream(archivePath);
        return SevenZipArchiveDecoder.TryDetectStreamEncryption(archive, out bool encrypted) == SevenZipArchiveDecodeResult.Ok
            && encrypted;
      }
      catch (System.IO.IOException)
      {
        return false;
      }
      catch (System.UnauthorizedAccessException)
      {
        return false;
      }
    });
  }

  // Потоковое AES-создание: строит опции из пароля и гарантированно освобождает пароль.
  private static SevenZipArchiveWriteResult BuildAesToStream(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      System.IO.Stream output,
      string? password,
      int dictionarySize,
      System.IProgress<SevenZipProgress>? progress,
      System.Threading.CancellationToken token,
      System.IProgress<SevenZipCompressionFileProgress>? currentFile)
  {
    if (password is null)
      return SevenZipArchiveWriteResult.InvalidData;

    SevenZipPassword sevenZipPassword = SevenZipPassword.FromString(password);
    try
    {
      var options = new SevenZipAesEncryptionOptions { Password = sevenZipPassword, CompressWithLzma2 = true };
      return SevenZipArchiveWriter.BuildAesArchiveToStream(entries, output, options, dictionarySize, progress, token, currentFile);
    }
    finally
    {
      sevenZipPassword.Dispose();
    }
  }

  // Открывает архив на чтение: если путь — первый том (.001 рядом), склеивает тома
  // через VolumeSpanningReadStream; иначе — обычный файловый поток.
  private static System.IO.Stream OpenArchiveReadStream(string archivePath)
  {
    if (VolumeSpanningReadStream.TryGetVolumeBasePath(archivePath, out string basePath))
      return new VolumeSpanningReadStream(basePath);

    return new System.IO.FileStream(archivePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);
  }

  // Готовит SevenZipDecodeOptions (с паролем или без) и гарантированно освобождает пароль.
  private static T WithOptions<T>(string? password, System.Func<SevenZipDecodeOptions, T> body)
  {
    SevenZipPassword? sevenZipPassword = null;

    try
    {
      SevenZipDecodeOptions options = password is null
          ? SevenZipDecodeOptions.Default
          : SevenZipDecodeOptions.WithPassword(sevenZipPassword = SevenZipPassword.FromString(password));

      return body(options);
    }
    finally
    {
      sevenZipPassword?.Dispose();
    }
  }
}
