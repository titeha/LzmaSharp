using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;

namespace Lzma.Core.SevenZip;

// BCJ2-путь writer-а: один folder на непустой файл. Folder содержит 4 coder-а — три LZMA2
// (сжимают потоки Main/Call/Jump) и BCJ2 (4 входа, 1 выход). Управляющий поток (Control)
// пишется сырым (unbound packed stream). Это совместимо с нашим декодером и с 7-Zip.
public static partial class SevenZipArchiveWriter
{
  // Method id BCJ2 в 7z: 03 03 01 1B.
  private static readonly byte[] _bcj2MethodId = [0x03, 0x03, 0x01, 0x1B];

  /// <summary>
  /// Строит 7z-архив, применяя к непустым файлам фильтр BCJ2 (x86): поток разбивается на
  /// Main/Call/Jump/Control, первые три досжимаются LZMA2. Для исполняемых файлов это сжимает
  /// плотнее обычного LZMA2 (адреса ветвлений становятся абсолютными и лучше предсказуемы).
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

      if (!TryLzma2Compress(streams.Main, out byte[] packedMain, out byte mainProp) ||
          !TryLzma2Compress(streams.Call, out byte[] packedCall, out byte callProp) ||
          !TryLzma2Compress(streams.Jump, out byte[] packedJump, out byte jumpProp))
        return SevenZipArchiveWriteResult.InternalError;

      // Порядок packed-стримов folder-а = порядок packed-индексов [0,1,2,6]:
      // lzma2(main), lzma2(call), lzma2(jump), control (сырой).
      foreach (byte[] stream in new[] { packedMain, packedCall, packedJump, streams.Control })
      {
        packedStreams.Add(stream);
        packSizes.Add(stream.Length);

        totalLength += stream.Length;
        if (totalLength > int.MaxValue)
          return SevenZipArchiveWriteResult.InternalError;
      }

      folderBodies[folderIndex] = BuildBcj2Lzma2FolderBody(mainProp, callProp, jumpProp);

      // CodersUnpackSize по out-стримам в порядке coder-ов: LZMA2(main)=Main, LZMA2(call)=Call,
      // LZMA2(jump)=Jump, BCJ2 = финальный (исходный файл).
      coderUnpackSizes[folderIndex] =
      [
          streams.Main.Length,
          streams.Call.Length,
          streams.Jump.Length,
          entry.Content.Length,
      ];

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

    // Переиспользуем общий (multi-folder) путь next header из GOST-пути (структурно generic).
    if (!TryBuildGostFoldersNextHeader(
            entries, packSizes.ToArray(), folderBodies, coderUnpackSizes, finalCrcs, out byte[] nextHeaderBytes))
      return SevenZipArchiveWriteResult.InternalError;

    archive = BuildArchiveWithPackedData(packedData, nextHeaderBytes);

    return SevenZipArchiveWriteResult.Ok;
  }

  // Сжимает один поток BCJ2 в LZMA2; размер словаря снапается вверх под размер потока.
  private static bool TryLzma2Compress(byte[] data, out byte[] compressed, out byte propertiesByte)
  {
    compressed = [];
    propertiesByte = 0;

    int requested = Math.Max(data.Length, 1 << 12); // не меньше минимального словаря 4 КБ

    if (!Lzma2Properties.TryCreateFromDictionarySize((uint)requested, out Lzma2Properties properties))
      return false;

    if (!properties.TryGetDictionarySizeInt32(out int dictionarySize))
      return false;

    propertiesByte = properties.DictionaryProp;
    compressed = Lzma2LzmaEncoder.Encode(data, new LzmaProperties(3, 0, 2), dictionarySize);
    return true;
  }

  /// <summary>
  /// Тело folder-а: 3 LZMA2-coder-а (Main/Call/Jump) + BCJ2 (4 входа, 1 выход).
  /// Bind pairs: BCJ2.in0←LZMA2(main).out, in1←LZMA2(call).out, in2←LZMA2(jump).out.
  /// Вход BCJ2.in3 (Control) — сырой packed stream. Packed-индексы: [0,1,2,6].
  /// </summary>
  private static byte[] BuildBcj2Lzma2FolderBody(byte mainProp, byte callProp, byte jumpProp)
  {
    List<byte> body = new(48);

    TryWriteUInt64(body, 4); // четыре coder-а

    AddLzma2Coder(body, mainProp); // coder 0: in 0, out 0
    AddLzma2Coder(body, callProp); // coder 1: in 1, out 1
    AddLzma2Coder(body, jumpProp); // coder 2: in 2, out 2

    // coder 3: BCJ2 (in 3..6, out 3). flags 0x14 = complex | idSize 4.
    body.Add(0x14);
    body.AddRange(_bcj2MethodId);
    TryWriteUInt64(body, 4); // numInStreams
    TryWriteUInt64(body, 1); // numOutStreams

    // Bind pairs (numBindPairs = totalOut - 1 = 3): InIndex (вход BCJ2) ← OutIndex (выход LZMA2).
    TryWriteUInt64(body, 3); TryWriteUInt64(body, 0);
    TryWriteUInt64(body, 4); TryWriteUInt64(body, 1);
    TryWriteUInt64(body, 5); TryWriteUInt64(body, 2);

    // Packed-стримы (numPackedStreams = totalIn - numBindPairs = 7 - 3 = 4 > 1 → пишем индексы):
    // входы LZMA2 (0,1,2) + сырой вход BCJ2 для Control (6).
    TryWriteUInt64(body, 0);
    TryWriteUInt64(body, 1);
    TryWriteUInt64(body, 2);
    TryWriteUInt64(body, 6);

    return [.. body];
  }

  // LZMA2-coder: flags 0x21 (idSize 1 | attributes 0x20), method id 0x21, props размер 1, props.
  private static void AddLzma2Coder(List<byte> body, byte propertiesByte)
  {
    body.Add(0x21);
    body.Add(Lzma2MethodId);
    body.Add(0x01);
    body.Add(propertiesByte);
  }
}
