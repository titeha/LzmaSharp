using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// LZMA2-путь writer-а: сжатие непустых файлов LZMA2. Контейнерная обвязка (PackInfo /
// UnpackInfo / FilesInfo) — в общем сжатом пути (SevenZipArchiveWriter.Compressed.cs).
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив с непустыми файлами, сжатыми LZMA2, с заданным размером словаря.
  /// </summary>
  /// <remarks>
  /// Запрошенный размер округляется вверх до ближайшего канонического размера LZMA2; этот же
  /// канонический размер используется как окно match finder-а (энкодер несёт словарь между
  /// чанками, поэтому больший словарь реально улучшает сжатие для входов больше словаря).
  /// </remarks>
  private static SevenZipArchiveWriteResult BuildLzma2EntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int dictionarySize,
      out byte[] archive,
      IProgress<SevenZipProgress>? progress = null,
      System.Threading.CancellationToken token = default)
  {
    archive = [];

    if (dictionarySize <= 0)
      return SevenZipArchiveWriteResult.InvalidData;

    if (!Lzma2Properties.TryCreateFromDictionarySize((uint)dictionarySize, out Lzma2Properties properties))
      return SevenZipArchiveWriteResult.InvalidData;

    // Наш энкодер работает с Int32-окном: словарь > 2 ГиБ пока не поддерживаем.
    if (!properties.TryGetDictionarySizeInt32(out int effectiveDictionarySize))
      return SevenZipArchiveWriteResult.NotSupported;

    var lzmaProperties = new LzmaProperties(3, 0, 2);

    // LZMA2 coder: flags = idSize(1) | attributes(0x20) = 0x21, method id = 0x21,
    // размер properties = 1, properties = байт размера словаря.
    byte[] coderBytes = [0x21, Lzma2MethodId, 0x01, properties.DictionaryProp];

    return BuildCompressedEntriesArchive(
        entries,
        // Токен захвачен в замыкании — отмена доходит внутрь чанк-цикла энкодера (per-chunk),
        // а не только между файлами (проверка в BuildCompressedEntriesArchive).
        content => Lzma2LzmaEncoder.Encode(content, lzmaProperties, effectiveDictionarySize, token: token),
        coderBytes,
        out archive,
        progress,
        token);
  }
}
