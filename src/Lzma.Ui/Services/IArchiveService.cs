using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Lzma.Core.SevenZip;

namespace Lzma.Ui.Services;

/// <summary>
/// Результат открытия (декодирования) архива: код результата ядра и набор записей.
/// </summary>
public readonly record struct ArchiveOpenOutcome(
    SevenZipArchiveDecodeResult Result,
    SevenZipDecodedEntry[] Entries);

/// <summary>
/// Результат создания архива: код результата ядра и собранные байты архива.
/// </summary>
public readonly record struct ArchiveCreateOutcome(
    SevenZipArchiveWriteResult Result,
    byte[] Archive);

/// <summary>
/// Единый шов операций над 7z-архивом для UI: открытие, извлечение, диагностика
/// (а в дальнейшем — создание, прогресс и т.д.).
/// </summary>
/// <remarks>
/// Назначение шва — изолировать <see cref="ViewModels.MainViewModel"/> от прямых вызовов
/// ядра и фоновой раскладки (<c>Task.Run</c>), чтобы новые возможности (создание архивов,
/// прогресс операций) подключались здесь, не размазываясь по модели представления и не
/// ломая её тесты. Реализация по умолчанию — <see cref="LzmaArchiveService"/>; тесты могут
/// подставлять собственную.
/// </remarks>
public interface IArchiveService
{
  /// <summary>Открывает (декодирует) архив в память с опциональным паролем.</summary>
  Task<ArchiveOpenOutcome> OpenAsync(byte[] bytes, string? password);

  /// <summary>
  /// Извлекает всё содержимое архива в указанную папку. Опциональный <paramref name="progress"/>
  /// получает отчёт о ходе извлечения (по folder-ам/файлам ядра).
  /// </summary>
  Task<SevenZipArchiveDecodeResult> ExtractAllAsync(
      byte[] bytes,
      string? password,
      string destination,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default);

  /// <summary>
  /// Собирает 7z-архив из набора записей выбранным методом сжатия. Возвращает байты архива;
  /// запись на диск — отдельный шаг (<see cref="WriteArchiveAsync"/>). Опциональный
  /// <paramref name="progress"/> получает отчёт о ходе сжатия (по исходным размерам файлов).
  /// </summary>
  Task<ArchiveCreateOutcome> CreateArchiveAsync(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      SevenZipWriterCompressionMethod method,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default);

  /// <summary>
  /// Записывает байты архива в файл. Возвращает <see langword="true"/> при успехе,
  /// <see langword="false"/> при ошибке ввода-вывода.
  /// </summary>
  Task<bool> WriteArchiveAsync(byte[] archive, string path);

  /// <summary>
  /// ПОТОКОВОЕ создание LZMA2-архива прямо в файл <paramref name="destinationPath"/>: данные каждой
  /// записи читаются из <see cref="SevenZipStreamingEntry.OpenRead"/> и сжимаются на лету, не
  /// удерживая файл/архив в памяти (поддержка файлов &gt; 2 ГиБ). Реализация по умолчанию —
  /// <see cref="SevenZipArchiveWriteResult.NotSupported"/> (шов подключается только в боевой службе).
  /// </summary>
  Task<SevenZipArchiveWriteResult> CreateArchiveToFileAsync(
      IReadOnlyList<SevenZipStreamingEntry> entries,
      string destinationPath,
      int dictionarySize,
      int maxDegreeOfParallelism = 0,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default)
      => Task.FromResult(SevenZipArchiveWriteResult.NotSupported);

  /// <summary>
  /// ПОТОКОВОЕ извлечение архива прямо из файла <paramref name="archivePath"/> в
  /// <paramref name="destination"/>, НЕ загружая архив в память (поддержка архивов &gt; 2 ГиБ).
  /// Реализация по умолчанию — <see cref="SevenZipArchiveDecodeResult.NotSupported"/>.
  /// </summary>
  Task<SevenZipArchiveDecodeResult> ExtractArchiveFileAsync(
      string archivePath,
      string destination,
      IProgress<SevenZipProgress>? progress = null,
      CancellationToken token = default)
      => Task.FromResult(SevenZipArchiveDecodeResult.NotSupported);

  /// <summary>
  /// Возвращает человекочитаемое описание методов архива (для диагностики при ошибке
  /// открытия) либо пустую строку, если описать не удалось.
  /// </summary>
  Task<string> DescribeMethodsAsync(byte[] bytes, string? password);
}
