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
