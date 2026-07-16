using System.Collections.Generic;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;

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
  public Task<SevenZipArchiveDecodeResult> ExtractAllAsync(
      byte[] bytes,
      string? password,
      string destination,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    return Task.Run(() => WithOptions(password, options =>
        SevenZipArchiveDecoder.ExtractToDirectory(bytes, options, destination, overwrite: false, out _, progress, token)), token);
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
      System.IProgress<string>? currentFile = null)
  {
    return Task.Run(() =>
    {
      try
      {
        // Пишем прямо в целевой файл потоком — ни файлы, ни архив в памяти не держим.
        using var output = new System.IO.FileStream(
            destinationPath, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite);

        // Диспетчер по методу: LZMA2/Auto — многопоточно; PPMd/Copy — пофайлово (PPMd последователен).
        return method switch
        {
          SevenZipWriterCompressionMethod.Lzma2 =>
              SevenZipArchiveWriter.BuildLzma2ArchiveToStream(
                  entries, output, dictionarySize, maxDegreeOfParallelism, progress, token, currentFile),
          SevenZipWriterCompressionMethod.Auto =>
              SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, output, dictionarySize, progress, token, currentFile),
          SevenZipWriterCompressionMethod.Ppmd =>
              SevenZipArchiveWriter.BuildPpmdArchiveToStream(entries, output, progress, token, currentFile),
          SevenZipWriterCompressionMethod.Copy =>
              SevenZipArchiveWriter.BuildCopyArchiveToStream(entries, output, progress, token, currentFile),
          _ => SevenZipArchiveWriteResult.NotSupported,
        };
      }
      catch (System.IO.IOException)
      {
        return SevenZipArchiveWriteResult.InternalError;
      }
      catch (System.UnauthorizedAccessException)
      {
        return SevenZipArchiveWriteResult.InternalError;
      }
    }, token);
  }

  /// <inheritdoc />
  public Task<SevenZipArchiveDecodeResult> ExtractArchiveFileAsync(
      string archivePath,
      string destination,
      System.IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    return Task.Run(() =>
    {
      try
      {
        // Читаем архив прямо из файла потоком — в память его не грузим (поддержка > 2 ГиБ).
        using var archive = new System.IO.FileStream(
            archivePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);

        return SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
            archive, SevenZipDecodeOptions.Default, destination, overwrite: false, progress, token);
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
        using var archive = new System.IO.FileStream(
            archivePath, System.IO.FileMode.Open, System.IO.FileAccess.Read);

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
  public Task<string> DescribeMethodsAsync(byte[] bytes, string? password)
  {
    return Task.Run(() =>
    {
      SevenZipArchiveInspector.TryDescribeMethods(bytes, password, out string description);
      return description ?? string.Empty;
    });
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
