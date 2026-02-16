namespace Lzma.Core.SevenZip;

/// <summary>
/// Информация о файлах из блока FilesInfo в заголовке 7z.
/// </summary>
public readonly struct SevenZipFilesInfo(ulong fileCount, string[]? names, bool[]? emptyStreams = null, bool[]? emptyFiles = null, bool[]? anti = null)
{
  /// <summary>
  /// Количество файлов в архиве.
  /// </summary>
  public ulong FileCount { get; } = fileCount;

  /// <summary>
  /// Имена файлов (если присутствует свойство <see cref="SevenZipNid.Name"/>).
  /// Длина массива равна <see cref="FileCount"/>.
  /// </summary>
  public string[]? Names { get; } = names;

  public bool HasNames => Names is not null;

  /// <summary>
  /// Вектор kEmptyStream длиной <see cref="FileCount"/> (true => у файла нет потока данных).
  /// Если свойство отсутствует — null.
  /// </summary>
  public bool[]? EmptyStreams { get; } = emptyStreams;

  public bool HasEmptyStreams => EmptyStreams is not null;

  /// <summary>
  /// kEmptyFile (только для EmptyStreams): true => пустой файл, false => директория (при EmptyStream=true).
  /// Массив длиной FileCount (для не-empty-stream элементов всегда false).
  /// </summary>
  public bool[]? EmptyFiles { get; } = emptyFiles;

  public bool HasEmptyFiles => EmptyFiles is not null;

  /// <summary>
  /// kAnti (только для EmptyStreams): true => anti-item.
  /// Массив длиной FileCount (для не-empty-stream элементов всегда false).
  /// </summary>
  public bool[]? Anti { get; } = anti;

  public bool HasAnti => Anti is not null;
}
