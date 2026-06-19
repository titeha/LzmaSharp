using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

// Общий путь writer-а для сжатых folder-ов: один folder = один непустой файл = один coder.
// Используется LZMA2- и PPMd-путями (различаются только энкодером и байтами coder-а).
public static partial class SevenZipArchiveWriter
{
  /// <summary>
  /// Строит 7z-архив, сжимая непустые файлы делегатом <paramref name="encode"/> и записывая
  /// folder с заданными байтами coder-а <paramref name="coderBytes"/> (flags + method id +
  /// размер properties + properties). Поддерживает как набор только из непустых файлов, так и
  /// mixed-набор с empty entries.
  /// </summary>
  private static SevenZipArchiveWriteResult BuildCompressedEntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      Func<byte[], byte[]> encode,
      byte[] coderBytes,
      out byte[] archive)
  {
    archive = [];

    int count = CountNonEmptyFiles(entries);
    var packSizes = new int[count];
    var unpackSizes = new int[count];
    var unpackCrcs = new uint[count];
    var compressedStreams = new List<byte[]>(count);

    long totalLength = 0;
    int streamIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      byte[] compressed = encode(entry.Content);

      compressedStreams.Add(compressed);
      packSizes[streamIndex] = compressed.Length;
      unpackSizes[streamIndex] = entry.Content.Length;
      unpackCrcs[streamIndex] = Crc32.Compute(entry.Content);

      totalLength += compressed.Length;
      if (totalLength > int.MaxValue)
        return SevenZipArchiveWriteResult.InternalError;

      streamIndex++;
    }

    byte[] packedData = new byte[(int)totalLength];
    int outputOffset = 0;
    for (int i = 0; i < compressedStreams.Count; i++)
    {
      compressedStreams[i].CopyTo(packedData.AsSpan(outputOffset));
      outputOffset += compressedStreams[i].Length;
    }

    if (!TryBuildCompressedEntriesNextHeader(entries, packSizes, unpackSizes, unpackCrcs, coderBytes, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>Строит next header для сжатого-folder сценария.</summary>
  private static bool TryBuildCompressedEntriesNextHeader(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      int[] packSizes,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte[] coderBytes,
      out byte[] nextHeaderBytes)
  {
    nextHeaderBytes = [];

    List<byte> header = new(256)
    {
        SevenZipNid.Header,
        SevenZipNid.MainStreamsInfo,
    };

    if (!TryWriteCompressedStreamsPackInfo(header, packSizes))
      return false;

    if (!TryWriteCompressedFoldersUnpackInfo(header, unpackSizes, unpackCrcs, coderBytes))
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

  /// <summary>Пишет PackInfo для packed stream-ов сжатого сценария (без CRC packed stream-ов).</summary>
  private static bool TryWriteCompressedStreamsPackInfo(List<byte> header, int[] packSizes)
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

  /// <summary>Пишет UnpackInfo для folder-ов сжатого сценария (по одному coder-у на folder).</summary>
  private static bool TryWriteCompressedFoldersUnpackInfo(
      List<byte> header,
      int[] unpackSizes,
      uint[] unpackCrcs,
      byte[] coderBytes)
  {
    header.Add(SevenZipNid.UnpackInfo);

    header.Add(SevenZipNid.Folder);

    if (!TryWriteUInt64(header, (ulong)unpackSizes.Length))
      return false;

    header.Add(0x00);

    for (int i = 0; i < unpackSizes.Length; i++)
    {
      if (!TryWriteUInt64(header, 1)) // один coder на folder
        return false;

      header.AddRange(coderBytes);
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
