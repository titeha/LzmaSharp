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

  /// <summary>Сжатие `PPMd` (вариант H / PPMd7, как в 7-Zip).</summary>
  Ppmd = 2,

  /// <summary>
  /// Автовыбор кодека по содержимому: преимущественно текстовые данные → `PPMd`
  /// (плотнее на тексте), иначе → `LZMA2`. Дешёвая эвристика (level 1).
  /// </summary>
  Auto = 3,
}
