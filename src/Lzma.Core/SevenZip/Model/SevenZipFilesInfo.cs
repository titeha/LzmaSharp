namespace Lzma.Core.SevenZip;

/// <summary>
/// Информация о файлах из блока FilesInfo в заголовке 7z.
/// </summary>
public readonly struct SevenZipFilesInfo(
  ulong fileCount,
  string[]? names,
  bool[]? emptyStreams = null,
  bool[]? emptyFiles = null,
  bool[]? anti = null,
  bool[]? crcDefined = null,
  uint[]? crc = null,
  bool[]? mTimeDefined = null,
  ulong[]? mTime = null,
  bool[]? winAttribDefined = null,
  uint[]? winAttrib = null,
  bool[]? cTimeDefined = null,
  ulong[]? cTime = null,
  bool[]? aTimeDefined = null,
  ulong[]? aTime = null)
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

  /// <summary>
  /// FilesInfo.kCRC: флаг "CRC определён" для каждого файла.
  /// Длина = FileCount. Если свойство отсутствует — null.
  /// </summary>
  public bool[]? CrcDefined { get; } = crcDefined;

  /// <summary>
  /// FilesInfo.kCRC: CRC32 для каждого файла.
  /// Длина = FileCount. Если CRC не определён (CrcDefined=false), значение может быть 0.
  /// </summary>
  public uint[]? Crc { get; } = crc;

  public bool HasCrc => CrcDefined is not null;

  /// <summary>
  /// FilesInfo.kMTime: флаг "время определено" для каждого файла.
  /// Длина = FileCount. Если свойство отсутствует — null.
  /// </summary>
  public bool[]? MTimeDefined { get; } = mTimeDefined;

  /// <summary>
  /// FilesInfo.kMTime: время (REAL_UINT64) для каждого файла.
  /// Значение хранится как сырой 64-битный timestamp из 7z (Windows FILETIME/NTFS time).
  /// Если время не определено (MTimeDefined=false), значение может быть 0.
  /// </summary>
  public ulong[]? MTime { get; } = mTime;

  public bool HasMTime => MTimeDefined is not null;

  /// <summary>
  /// FilesInfo.kWinAttributes: флаг "атрибуты определены" для каждого файла.
  /// Длина = FileCount. Если свойство отсутствует — null.
  /// </summary>
  public bool[]? WinAttribDefined { get; } = winAttribDefined;

  /// <summary>
  /// FilesInfo.kWinAttributes: UINT32 атрибуты для каждого файла.
  /// Если атрибуты не определены (WinAttribDefined=false), значение может быть 0.
  /// </summary>
  public uint[]? WinAttrib { get; } = winAttrib;

  public bool HasWinAttrib => WinAttribDefined is not null;

  public bool[]? CTimeDefined { get; } = cTimeDefined;
  public ulong[]? CTime { get; } = cTime;
  public bool HasCTime => CTimeDefined is not null;

  public bool[]? ATimeDefined { get; } = aTimeDefined;
  public ulong[]? ATime { get; } = aTime;
  public bool HasATime => ATimeDefined is not null;
}
