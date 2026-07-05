using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Lzma.Ui.Services;

/// <summary>
/// Абстракция выбора папки-источника для упаковки. В отличие от <see cref="ISourceFilesPicker"/>
/// рекурсивно собирает все файлы внутри выбранной папки; имена записей — относительные пути
/// (верхний сегмент — имя самой папки). Отделяет ViewModel от диалога и обхода диска для тестов.
/// </summary>
public interface ISourceFolderPicker
{
  /// <summary>
  /// Просит выбрать папку и возвращает её файлы как <see cref="PickedFile"/> с относительными
  /// именами. <see langword="null"/> — выбор отменён или в папке нет файлов. Опциональный
  /// <paramref name="progress"/> получает живой счётчик по мере обхода и чтения файлов;
  /// <paramref name="token"/> отменяет чтение (бросает <see cref="OperationCanceledException"/>).
  /// </summary>
  Task<IReadOnlyList<PickedFile>?> PickFolderFilesAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default);

  /// <summary>Поддерживает ли пикер потоковый выбор (по ссылкам, без чтения в память).</summary>
  bool SupportsRefs => false;

  /// <summary>
  /// Потоковый вариант <see cref="PickFolderFilesAsync"/>: возвращает ссылки на файлы папки
  /// (относительное имя+размер+фабрика потока), НЕ читая их в память. По умолчанию не поддерживается.
  /// </summary>
  Task<IReadOnlyList<PickedFileRef>?> PickFolderFileRefsAsync(
      IProgress<ScanProgress>? progress = null, CancellationToken token = default)
      => Task.FromResult<IReadOnlyList<PickedFileRef>?>(null);
}
