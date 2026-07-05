using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>Выбранный исходный файл для добавления в архив: имя записи и содержимое.</summary>
public sealed record PickedFile(string Name, byte[] Bytes);

/// <summary>
/// Ссылка на исходный файл для ПОТОКОВОГО добавления в архив: имя, размер и фабрика открытия потока
/// данных (файл НЕ читается в память — позволяет паковать файлы &gt; 2 ГиБ).
/// </summary>
public sealed record PickedFileRef(string Name, long Length, Func<Stream> OpenRead);

/// <summary>
/// Абстракция выбора исходных файлов для упаковки. Отделяет ViewModel от платформенного
/// диалога множественного выбора, чтобы логику создания архива можно было тестировать без UI.
/// </summary>
public interface ISourceFilesPicker
{
  /// <summary>
  /// Просит выбрать один или несколько файлов. Возвращает <see langword="null"/>, если выбор
  /// отменён (пустой список трактуется так же — добавлять нечего). Опциональный
  /// <paramref name="progress"/> получает живой счётчик по мере чтения файлов в память;
  /// <paramref name="token"/> отменяет чтение (бросает <see cref="OperationCanceledException"/>).
  /// </summary>
  Task<IReadOnlyList<PickedFile>?> PickFilesAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default);

  /// <summary>Поддерживает ли пикер потоковый выбор (по ссылкам, без чтения в память).</summary>
  bool SupportsRefs => false;

  /// <summary>
  /// Потоковый вариант <see cref="PickFilesAsync"/>: возвращает ссылки на файлы (имя+размер+фабрика
  /// потока), НЕ читая их в память. По умолчанию не поддерживается (<see langword="null"/>).
  /// </summary>
  Task<IReadOnlyList<PickedFileRef>?> PickFileRefsAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default)
      => Task.FromResult<IReadOnlyList<PickedFileRef>?>(null);
}
