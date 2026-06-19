using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// LZMA2-путь writer-а: сжатие непустых файлов LZMA2. Контейнерная обвязка (PackInfo /
// UnpackInfo / FilesInfo) — в общем сжатом пути (SevenZipArchiveWriter.Compressed.cs).
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив с непустыми файлами, сжатыми LZMA2.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildLzma2EntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (!Lzma2Properties.TryEncode(Lzma2DictionarySize, out byte propertiesByte))
      return SevenZipArchiveWriteResult.InternalError;

    var lzmaProperties = new LzmaProperties(3, 0, 2);

    // LZMA2 coder: flags = idSize(1) | attributes(0x20) = 0x21, method id = 0x21,
    // размер properties = 1, properties = байт размера словаря.
    byte[] coderBytes = [0x21, Lzma2MethodId, 0x01, propertiesByte];

    return BuildCompressedEntriesArchive(
        entries,
        content => Lzma2LzmaEncoder.Encode(content, lzmaProperties, Lzma2DictionarySize),
        coderBytes,
        out archive);
  }
}
