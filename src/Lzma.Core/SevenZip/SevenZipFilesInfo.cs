namespace Lzma.Core.SevenZip;

/// <summary>
/// Информация о файлах из блока FilesInfo в заголовке 7z.
/// </summary>
public readonly struct SevenZipFilesInfo(ulong fileCount, string[]? names, bool[]? emptyStreams)
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

  public bool HasEmptyStreams => EmptyStreams is not null;

  /// <summary>
  /// Вектор kEmptyStream длиной <see cref="FileCount"/> (true => у файла нет потока данных).
  /// Если свойство отсутствует — null.
  /// </summary>
  public bool[]? EmptyStreams { get; } = emptyStreams;

  public SevenZipFilesInfo(ulong fileCount, string[]? names)
        : this(fileCount, names, emptyStreams: null)
  {
  }

}
