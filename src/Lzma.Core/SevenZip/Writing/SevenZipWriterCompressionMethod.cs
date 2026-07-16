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

  /// <summary>
  /// Фильтр `BCJ2` (x86) + `LZMA2`: исполняемые файлы (`.exe`/`.dll`) жмутся плотнее — адреса
  /// ветвлений становятся абсолютными и лучше предсказуемы. Применяется к каждому непустому файлу.
  /// </summary>
  Bcj2 = 4,

  /// <summary>
  /// Шифрование `7zAES` (AES-256) поверх `LZMA2`: каждый непустой файл сжимается и шифруется.
  /// Требует пароль. Совместимо с настоящим 7-Zip.
  /// </summary>
  Aes = 5,
}
