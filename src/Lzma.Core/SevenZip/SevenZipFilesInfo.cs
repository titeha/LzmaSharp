namespace Lzma.Core.SevenZip;

/// <summary>
/// Информация о файлах из блока FilesInfo в заголовке 7z.
/// </summary>
public readonly struct SevenZipFilesInfo(ulong fileCount, string[]? names)
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
}
