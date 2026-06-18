namespace Lzma.Core.SevenZip;

/// <summary>
/// Метод сжатия для непустых файлов при записи 7z-архива.
/// </summary>
public enum SevenZipWriterCompressionMethod
{
  /// <summary>Без сжатия (`Copy`).</summary>
  Copy = 0,

  /// <summary>Сжатие `LZMA2`.</summary>
  Lzma2 = 1,
}
