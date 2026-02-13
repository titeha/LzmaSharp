using System.Text;

namespace Lzma.Core.SevenZip;

public static class SevenZipFilesInfoReader
{
  public static SevenZipFilesInfoReadResult TryRead(
    ReadOnlySpan<byte> src,
    out SevenZipFilesInfo filesInfo,
    out int bytesConsumed)
  {
    filesInfo = default;
    bytesConsumed = 0;

    if (src.Length == 0)
      return SevenZipFilesInfoReadResult.NeedMoreInput;

    int offset = 0;

    if (src[offset++] != SevenZipNid.FilesInfo)
      return SevenZipFilesInfoReadResult.InvalidData;

    var r = SevenZipEncodedUInt64.TryRead(src[offset..], out ulong fileCount, out int readBytes);
    if (r == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
      return SevenZipFilesInfoReadResult.NeedMoreInput;

    offset += readBytes;

    if (fileCount > int.MaxValue)
      return SevenZipFilesInfoReadResult.NotSupported;

    int fileCountInt = (int)fileCount;

    string[]? names = null;
    bool[]? emptyStreams = null;

    while (true)
    {
      if (offset >= src.Length)
        return SevenZipFilesInfoReadResult.NeedMoreInput;

      byte nid = src[offset++];

      if (nid == SevenZipNid.End)
        break;

      r = SevenZipEncodedUInt64.TryRead(src[offset..], out ulong sizeU64, out readBytes);
      if (r == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
        return SevenZipFilesInfoReadResult.NeedMoreInput;

      offset += readBytes;

      if (sizeU64 > int.MaxValue)
        return SevenZipFilesInfoReadResult.NotSupported;

      int size = (int)sizeU64;

      if ((uint)size > (uint)(src.Length - offset))
        return SevenZipFilesInfoReadResult.NeedMoreInput;

      ReadOnlySpan<byte> payload = src.Slice(offset, size);

      if (nid == SevenZipNid.Name)
      {
        // kName: [external:byte] + UTF-16LE строки с '\0' после каждой
        if (names is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var nameRes = TryParseNames(payload, fileCountInt, out names);
        if (nameRes != SevenZipFilesInfoReadResult.Ok)
          return nameRes;
      }

      if (nid == SevenZipNid.EmptyStream)
      {
        if (emptyStreams is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var vecRes = TryParseBoolVector(payload, fileCountInt, out emptyStreams);
        if (vecRes != SevenZipFilesInfoReadResult.Ok)
          return vecRes;
      }

      // Пропускаем данные свойства (в т.ч. kName, мы уже распарсили payload).
      offset += size;
    }

    filesInfo = new SevenZipFilesInfo(fileCount, names, emptyStreams);
    bytesConsumed = offset;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseNames(
    ReadOnlySpan<byte> payload,
    int fileCount,
    out string[]? names)
  {
    names = null;

    if (payload.Length < 1)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte external = payload[0];
    if (external != 0)
      return SevenZipFilesInfoReadResult.NotSupported;

    ReadOnlySpan<byte> nameBytes = payload[1..];

    if (fileCount == 0)
    {
      // Нет файлов — не должно быть имён.
      if (nameBytes.Length != 0)
        return SevenZipFilesInfoReadResult.InvalidData;

      names = [];
      return SevenZipFilesInfoReadResult.Ok;
    }

    if ((nameBytes.Length & 1) != 0)
      return SevenZipFilesInfoReadResult.InvalidData;

    var result = new string[fileCount];
    int nameIndex = 0;

    // Быстрое преобразование UTF-16LE -> строки без лишней аллокации на весь буфер.
    // Читаем по 2 байта (char) и собираем строки до нулевого символа.
    var sb = new StringBuilder(capacity: 32);

    for (int i = 0; i < nameBytes.Length; i += 2)
    {
      char ch = (char)(nameBytes[i] | (nameBytes[i + 1] << 8));

      if (ch == '\0')
      {
        if (nameIndex >= fileCount)
          return SevenZipFilesInfoReadResult.InvalidData;

        result[nameIndex++] = sb.ToString();
        sb.Clear();
      }
      else
        sb.Append(ch);
    }

    // Последнее имя должно заканчиваться нулём (т.е. sb должен быть пустым).
    if (sb.Length != 0)
      return SevenZipFilesInfoReadResult.InvalidData;

    if (nameIndex != fileCount)
      return SevenZipFilesInfoReadResult.InvalidData;

    names = result;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseBoolVector(ReadOnlySpan<byte> payload, int count, out bool[]? vector)
  {
    vector = null;

    if (payload.Length < 1)
      return SevenZipFilesInfoReadResult.InvalidData;

    // Формат из SDK: byte allAreDefined; если 1 => все значения true.
    // Если 0 => далее битовый массив (старшие биты вперёд, 0x80..0x01).
    byte allAreDefined = payload[0];

    if (allAreDefined == 1)
    {
      // Строго: ожидаем ровно 1 байт полезной нагрузки.
      if (payload.Length != 1)
        return SevenZipFilesInfoReadResult.InvalidData;

      bool[] v = new bool[count];
      Array.Fill(v, true);
      vector = v;
      return SevenZipFilesInfoReadResult.Ok;
    }

    if (allAreDefined != 0)
      return SevenZipFilesInfoReadResult.InvalidData;

    int bytesRequired = (count + 7) / 8;

    // Строго: payload = 1 + ceil(count/8).
    if (payload.Length != 1 + bytesRequired)
      return SevenZipFilesInfoReadResult.InvalidData;

    bool[] result = new bool[count];

    int index = 1;
    byte mask = 0;
    byte b = 0;

    for (int i = 0; i < count; i++)
    {
      if (mask == 0)
      {
        b = payload[index++];
        mask = 0x80;
      }

      result[i] = (b & mask) != 0;
      mask >>= 1;
    }

    vector = result;
    return SevenZipFilesInfoReadResult.Ok;
  }
}
