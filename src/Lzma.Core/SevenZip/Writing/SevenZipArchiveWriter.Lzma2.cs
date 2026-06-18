using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// LZMA2-путь writer-а: сжатие непустых файлов LZMA2 и формирование соответствующих
// PackInfo / UnpackInfo. См. основной файл SevenZipArchiveWriter.cs.
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив с непустыми файлами, сжатыми LZMA2.
  /// Поддерживает как набор только из непустых файлов, так и mixed-набор с empty entries.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildLzma2EntriesArchive(IReadOnlyList<SevenZipArchiveWriterEntry> entries, out byte[] archive)
  {
    archive = [];

    if (!TryBuildLzma2PackedData(
            entries,
            out byte[] packedData,
            out int[] packSizes,
            out int[] unpackSizes,
            out uint[] unpackCrcs,
            out byte propertiesByte))
      return SevenZipArchiveWriteResult.InternalError;

    if (!TryBuildLzma2EntriesNextHeader(
            entries,
            packSizes,
            unpackSizes,
            unpackCrcs,
            propertiesByte,
            out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Сжимает непустые файлы LZMA2 и собирает packed data, размеры и CRC.
  /// </summary>
  /// <remarks>
  /// На каждый непустой файл — свой folder (свой packed stream). packSizes — размеры
  /// сжатых потоков, unpackSizes/unpackCrcs — размер и CRC исходных (несжатых) данных.
  /// </remarks>
  private static bool TryBuildLzma2PackedData(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] packedData,
      out int[] packSizes,
      out int[] unpackSizes,
      out uint[] unpackCrcs,
      out byte propertiesByte)
  {
    packedData = [];
    packSizes = [];
    unpackSizes = [];
    unpackCrcs = [];
    propertiesByte = 0;

    if (!Lzma2Properties.TryEncode(Lzma2DictionarySize, out propertiesByte))
      return false;

    var lzmaProperties = new LzmaProperties(3, 0, 2);

    int count = CountNonEmptyFiles(entries);
    packSizes = new int[count];
    unpackSizes = new int[count];
    unpackCrcs = new uint[count];

    var compressedStreams = new List<byte[]>(count);
    long totalLength = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      byte[] compressed = Lzma2LzmaEncoder.Encode(
          entry.Content,
          lzmaProperties,
          Lzma2DictionarySize);

      compressedStreams.Add(compressed);

      packSizes[streamIndex] = compressed.Length;
      unpackSizes[streamIndex] = entry.Content.Length;
      unpackCrcs[streamIndex] = Crc32.Compute(entry.Content);

      totalLength += compressed.Length;

      if (totalLength > int.MaxValue)
        return false;

      streamIndex++;
    }

    packedData = new byte[(int)totalLength];

    int outputOffset = 0;
    for (int i = 0; i < compressedStreams.Count; i++)
    {
      compressedStreams[i].CopyTo(packedData.AsSpan(outputOffset));
      outputOffset += compressedStreams[i].Length;
    }

    return true;
  }

  /// <summary>
  /// Строит next header для LZMA2-сценария.
  /// </summary>
  private static bool TryBuildLzma2EntriesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] packSizes,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte propertiesByte,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteLzma2StreamsPackInfo(header, packSizes))
      return false;

    if (!TryWriteLzma2FoldersUnpackInfo(header, unpackSizes, unpackCrcs, propertiesByte))
      return false;

    header.Add(SevenZipNid.End);

    if (AllEntriesAreNonEmptyFiles(entries))
    {
      if (!TryWriteAllNonEmptyCopyEntriesFilesInfo(header, entries))
        return false;
    }
    else if (!TryWriteMixedCopyEntriesFilesInfo(header, entries))
      return false;

    header.Add(SevenZipNid.End);

    nextHeaderBytes = [.. header];

    return true;
  }

  /// <summary>
  /// Пишет PackInfo для packed stream-ов LZMA2-сценария (без CRC packed stream-ов).
  /// </summary>
  private static bool TryWriteLzma2StreamsPackInfo(
      List<byte> header,
      int[] packSizes)
  {
    header.Add(SevenZipNid.PackInfo);

    if (!TryWriteUInt64(header, 0))
      return false;

    if (!TryWriteUInt64(header, (ulong)packSizes.Length))
      return false;

    header.Add(SevenZipNid.Size);

    for (int i = 0; i < packSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, (ulong)packSizes[i]))
        return false;
    }

    header.Add(SevenZipNid.End);

    return true;
  }

  /// <summary>
  /// Пишет UnpackInfo для folder-ов LZMA2-сценария.
  /// </summary>
  private static bool TryWriteLzma2FoldersUnpackInfo(
      List<byte> header,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte propertiesByte)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)unpackSizes.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, 1))
        return false;

      // LZMA2 coder: flags = idSize(1) | attributes(0x20) = 0x21, method id = 0x21,
      // properties size = 1, properties = байт размера словаря.
      header.Add(0x21);
      header.Add(Lzma2MethodId);
      header.Add(0x01);
      header.Add(propertiesByte);
    }

    header.Add(SevenZipNid.CodersUnpackSize);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, (ulong)unpackSizes[i]))
        return false;
    }

    header.Add(SevenZipNid.Crc);
    WriteAllDefinedCrcDigests(header, unpackCrcs);

    header.Add(SevenZipNid.End);

    return true;
  }
}
