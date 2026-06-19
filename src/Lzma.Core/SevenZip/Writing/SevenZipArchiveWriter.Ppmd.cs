using Lzma.Core.Ppmd;

namespace Lzma.Core.SevenZip;

// PPMd-путь writer-а (PPMd var.H / 7z). Контейнерная обвязка — в общем сжатом пути
// (SevenZipArchiveWriter.Compressed.cs).
public static partial class SevenZipArchiveWriter
{
  // PPMd (7z) order и размер памяти модели — как по умолчанию у 7-Zip.
  private const int PpmdOrder = 6;
  private const uint PpmdMemSize = 16u << 20; // 16 МБ

  /// <summary>
  /// Строит 7z-архив с непустыми файлами, сжатыми PPMd (var.H / PPMd7).
  /// </summary>
  private static SevenZipArchiveWriteResult BuildPpmdEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    // PPMd coder: flags = idSize(3) | attributes(0x20) = 0x23, method id = 03 04 01,
    // размер properties = 5, properties = [order, memSize (UInt32 LE)].
    byte[] coderBytes =
    [
        0x23,
        0x03, 0x04, 0x01,
        0x05,
        (byte)PpmdOrder,
        (byte)(PpmdMemSize & 0xFF),
        (byte)((PpmdMemSize >> 8) & 0xFF),
        (byte)((PpmdMemSize >> 16) & 0xFF),
        (byte)((PpmdMemSize >> 24) & 0xFF),
    ];

    return BuildCompressedEntriesArchive(entries, EncodePpmd, coderBytes, out archive);
  }

  private static byte[] EncodePpmd(byte[] content)
  {
    Ppmd7Encoder.Encode(content, PpmdOrder, PpmdMemSize, out byte[] output);
    return output;
  }
}
