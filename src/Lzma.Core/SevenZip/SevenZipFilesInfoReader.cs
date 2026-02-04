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

      // Пропускаем данные свойства (в т.ч. kName, мы уже распарсили payload).
      offset += size;
    }

    filesInfo = new SevenZipFilesInfo(fileCount, names);
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
}
