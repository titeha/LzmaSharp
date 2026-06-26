using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

// BCJ2-путь writer-а: один folder на непустой файл, в folder-е — BCJ2-coder (4 входа, 1 выход).
// Шаг 2a (текущий): четыре потока (Main/Call/Jump/Control) пишутся СЫРЫМИ (без LZMA-сжатия) —
// это валидирует folder-граф с 4 packed-стримами. Сжатие Main/Call/Jump добавится отдельным шагом.
public static partial class SevenZipArchiveWriter
{
  // Method id BCJ2 в 7z: 03 03 01 1B.
  private static readonly byte[] _bcj2MethodId = [0x03, 0x03, 0x01, 0x1B];

  /// <summary>
  /// Строит 7z-архив, применяя к непустым файлам фильтр BCJ2 (x86). На шаге 2a потоки BCJ2
  /// хранятся без сжатия — архив валиден и читается нашим декодером и 7-Zip, но не компактен.
  /// </summary>
  public static SevenZipArchiveWriteResult BuildBcj2Archive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    if (entries is null)
      return SevenZipArchiveWriteResult.InvalidData;

    if (entries.Count == 0)
      return BuildEmptyArchive(out archive);

    if (!TryValidateWriterEntries(entries))
      return SevenZipArchiveWriteResult.InvalidData;

    if (AllEntriesHaveNoContent(entries))
      return BuildEmptyEntriesArchive(entries, out archive);

    return BuildBcj2EntriesArchive(entries, out archive);
  }

  private static SevenZipArchiveWriteResult BuildBcj2EntriesArchive(
      IReadOnlyList<SevenZipArchiveWriterEntry> entries,
      out byte[] archive)
  {
    archive = [];

    int count = CountNonEmptyFiles(entries);

    var packedStreams = new List<byte[]>(count * 4);
    var packSizes = new List<int>(count * 4);
    var folderBodies = new byte[count][];
    var coderUnpackSizes = new int[count][];
    var finalCrcs = new uint[count];

    long totalLength = 0;
    int folderIndex = 0;

    for (int i = 0; i < entries.Count; i++)
    {
      SevenZipArchiveWriterEntry entry = entries[i];

      if (!IsNonEmptyFile(entry))
        continue;

      SevenZipBcj2Streams streams = SevenZipBcj2Encoder.Encode(entry.Content);

      // Порядок packed-стримов folder-а = порядок индексов входов BCJ2: main, call, jump, control.
      foreach (byte[] stream in new[] { streams.Main, streams.Call, streams.Jump, streams.Control })
      {
        packedStreams.Add(stream);
        packSizes.Add(stream.Length);

        totalLength += stream.Length;
        if (totalLength > int.MaxValue)
          return SevenZipArchiveWriteResult.InternalError;
      }

      folderBodies[folderIndex] = BuildBcj2RawFolderBody();
      coderUnpackSizes[folderIndex] = [entry.Content.Length];
      finalCrcs[folderIndex] = Crc32.Compute(entry.Content);

      folderIndex++;
    }

    byte[] packedData = new byte[(int)totalLength];
    int outputOffset = 0;
    for (int i = 0; i < packedStreams.Count; i++)
    {
      packedStreams[i].CopyTo(packedData.AsSpan(outputOffset));
      outputOffset += packedStreams[i].Length;
    }

    // Переиспользуем общий (multi-folder) путь next header: PackInfo с плоским списком pack-размеров +
    // UnpackInfo с телами folder-ов и их CodersUnpackSize + FilesInfo. (Метод назван ...Gost..., но
    // структурно generic — переименование отложено.)
    if (!TryBuildGostFoldersNextHeader(
            entries, packSizes.ToArray(), folderBodies, coderUnpackSizes, finalCrcs, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  /// <summary>
  /// Тело folder-а с единственным BCJ2-coder-ом (4 входа, 1 выход), все 4 входа — сырые
  /// packed-стримы. Bind pair-ов нет; packed-индексы [0,1,2,3] (порядок main/call/jump/control).
  /// </summary>
  private static byte[] BuildBcj2RawFolderBody()
  {
    List<byte> body = new(16);

    TryWriteUInt64(body, 1); // один coder

    // flags: complex (0x10) | idSize 4 (0x04) = 0x14.
    body.Add(0x14);
    body.AddRange(_bcj2MethodId);

    TryWriteUInt64(body, 4); // numInStreams
    TryWriteUInt64(body, 1); // numOutStreams

    // numBindPairs = totalOut - 1 = 0 → не пишем.
    // numPackedStreams = totalIn - numBindPairs = 4 > 1 → пишем индексы входов.
    TryWriteUInt64(body, 0);
    TryWriteUInt64(body, 1);
    TryWriteUInt64(body, 2);
    TryWriteUInt64(body, 3);

    return [.. body];
  }
}
