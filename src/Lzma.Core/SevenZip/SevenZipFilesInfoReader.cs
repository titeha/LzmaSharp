using System.Buffers.Binary;
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

    bool[]? crcDefined = null;
    uint[]? crc = null;

    bool[]? mTimeDefined = null;
    ulong[]? mTime = null;

    bool[]? winAttribDefined = null;
    uint[]? winAttrib = null;

    bool[]? cTimeDefined = null;
    ulong[]? cTime = null;

    bool[]? aTimeDefined = null;
    ulong[]? aTime = null;

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
    bool[]? emptyFiles = null;
    bool[]? anti = null;

    bool emptyFileSeen = false;
    bool antiSeen = false;
    ReadOnlySpan<byte> emptyFilePayload = default;
    ReadOnlySpan<byte> antiPayload = default;

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

      if (size > src.Length - offset)
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

        var vecRes = TryParseEmptyStreamVector(payload, fileCountInt, out emptyStreams);
        if (vecRes != SevenZipFilesInfoReadResult.Ok)
          return vecRes;

        if (emptyFileSeen && emptyFiles is null)
        {
          var res = TryParseEmptyStreamsSubVector(emptyFilePayload, emptyStreams, fileCountInt, out emptyFiles);
          if (res != SevenZipFilesInfoReadResult.Ok)
            return res;
        }

        if (antiSeen && anti is null)
        {
          var res = TryParseEmptyStreamsSubVector(antiPayload, emptyStreams, fileCountInt, out anti);
          if (res != SevenZipFilesInfoReadResult.Ok)
            return res;
        }
      }

      if (nid == SevenZipNid.EmptyFile)
      {
        if (emptyFileSeen)
          return SevenZipFilesInfoReadResult.InvalidData;

        emptyFileSeen = true;
        emptyFilePayload = payload;

        if (emptyStreams is not null)
        {
          var res = TryParseEmptyStreamsSubVector(payload, emptyStreams, fileCountInt, out emptyFiles);
          if (res != SevenZipFilesInfoReadResult.Ok)
            return res;
        }
      }

      if (nid == SevenZipNid.Anti)
      {
        if (antiSeen)
          return SevenZipFilesInfoReadResult.InvalidData;

        antiSeen = true;
        antiPayload = payload;

        if (emptyStreams is not null)
        {
          var res = TryParseEmptyStreamsSubVector(payload, emptyStreams, fileCountInt, out anti);
          if (res != SevenZipFilesInfoReadResult.Ok)
            return res;
        }
      }

      if (nid == SevenZipNid.Crc)
      {
        // kCRC в FilesInfo: Digests(NumFiles)
        // BYTE AllAreDefined
        // if (AllAreDefined == 0) { for(NumFiles) BIT Defined }
        // UINT32 CRCs[NumDefined]
        if (crcDefined is not null || crc is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var crcRes = TryParseCrcDigest(payload, fileCountInt, out crcDefined, out crc);
        if (crcRes != SevenZipFilesInfoReadResult.Ok)
          return crcRes;
      }

      if (nid == SevenZipNid.MTime)
      {
        // kMTime: BYTE AllAreDefined; [bits if 0]; BYTE External; [DataIndex if External!=0]; Times[NumDefined] (REAL_UINT64)
        if (mTimeDefined is not null || mTime is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var timeRes = TryParseTimeProperty(payload, fileCountInt, out mTimeDefined, out mTime);
        if (timeRes != SevenZipFilesInfoReadResult.Ok)
          return timeRes;
      }

      if (nid == SevenZipNid.CTime)
      {
        if (cTimeDefined is not null || cTime is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var res = TryParseTimeProperty(payload, fileCountInt, out cTimeDefined, out cTime);
        if (res != SevenZipFilesInfoReadResult.Ok)
          return res;
      }

      if (nid == SevenZipNid.ATime)
      {
        if (aTimeDefined is not null || aTime is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var res = TryParseTimeProperty(payload, fileCountInt, out aTimeDefined, out aTime);
        if (res != SevenZipFilesInfoReadResult.Ok)
          return res;
      }

      if (nid == SevenZipNid.WinAttrib)
      {
        // kWinAttributes: BYTE AllAreDefined; [bits if 0]; BYTE External; [DataIndex if External!=0]; Attrs[NumDefined] (UINT32)
        if (winAttribDefined is not null || winAttrib is not null)
          return SevenZipFilesInfoReadResult.InvalidData;

        var attrRes = TryParseWinAttribProperty(payload, fileCountInt, out winAttribDefined, out winAttrib);
        if (attrRes != SevenZipFilesInfoReadResult.Ok)
          return attrRes;
      }

      // Пропускаем данные свойства (в т.ч. kName, мы уже распарсили payload).
      offset += size;
    }

    if (emptyFileSeen && emptyFiles is null)
    {
      var res = TryParseEmptyStreamsSubVector(emptyFilePayload, emptyStreams, fileCountInt, out emptyFiles);
      if (res != SevenZipFilesInfoReadResult.Ok)
        return res;
    }

    if (antiSeen && anti is null)
    {
      var res = TryParseEmptyStreamsSubVector(antiPayload, emptyStreams, fileCountInt, out anti);
      if (res != SevenZipFilesInfoReadResult.Ok)
        return res;
    }

    filesInfo = new SevenZipFilesInfo(
      fileCount,
      names,
      emptyStreams,
      emptyFiles,
      anti,
      crcDefined,
      crc,
      mTimeDefined,
      mTime,
      winAttribDefined,
      winAttrib,
      cTimeDefined,
      cTime,
      aTimeDefined,
      aTime);
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

  private static SevenZipFilesInfoReadResult TryParseCrcDigest(
  ReadOnlySpan<byte> payload,
  int fileCount,
  out bool[]? defined,
  out uint[]? crc)
  {
    defined = null;
    crc = null;

    if (payload.Length < 1)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte allAreDefined = payload[0];
    int offset = 1;

    bool[] def = new bool[fileCount];
    int definedCount = 0;

    if (allAreDefined == 1)
    {
      Array.Fill(def, true);
      definedCount = fileCount;
    }
    else if (allAreDefined == 0)
    {
      int definedBytes = (fileCount + 7) / 8;

      // Строго: минимум должен быть хотя бы байты битового массива.
      if (payload.Length < 1 + definedBytes)
        return SevenZipFilesInfoReadResult.InvalidData;

      for (int i = 0; i < fileCount; i++)
      {
        byte b = payload[offset + (i >> 3)];
        byte mask = (byte)(0x80 >> (i & 7));
        bool isDef = (b & mask) != 0;
        def[i] = isDef;
        if (isDef)
          definedCount++;
      }

      offset += definedBytes;
    }
    else
      return SevenZipFilesInfoReadResult.InvalidData;

    ulong crcBytesU64 = (ulong)definedCount * 4UL;

    // Строго: payload должен заканчиваться ровно на CRCs.
    if (crcBytesU64 != (ulong)(payload.Length - offset))
      return SevenZipFilesInfoReadResult.InvalidData;

    uint[] values = new uint[fileCount];

    for (int i = 0; i < fileCount; i++)
    {
      if (!def[i])
        continue;

      values[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
      offset += 4;
    }

    if (offset != payload.Length)
      return SevenZipFilesInfoReadResult.InvalidData;

    defined = def;
    crc = values;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseTimeProperty(
  ReadOnlySpan<byte> payload,
  int fileCount,
  out bool[]? defined,
  out ulong[]? times)
  {
    defined = null;
    times = null;

    // минимум: AllAreDefined + External
    if (payload.Length < 2)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte allAreDefined = payload[0];
    int offset = 1;

    bool[] def = new bool[fileCount];
    int definedCount = 0;

    if (allAreDefined == 1)
    {
      Array.Fill(def, true);
      definedCount = fileCount;
    }
    else if (allAreDefined == 0)
    {
      int definedBytes = (fileCount + 7) / 8;

      // нужно минимум: AllAreDefined + bits + External
      if (payload.Length < 1 + definedBytes + 1)
        return SevenZipFilesInfoReadResult.InvalidData;

      for (int i = 0; i < fileCount; i++)
      {
        byte b = payload[offset + (i >> 3)];
        byte mask = (byte)(0x80 >> (i & 7));
        bool isDef = (b & mask) != 0;
        def[i] = isDef;
        if (isDef)
          definedCount++;
      }

      offset += definedBytes;
    }
    else
    {
      return SevenZipFilesInfoReadResult.InvalidData;
    }

    if (offset >= payload.Length)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte external = payload[offset++];
    if (external != 0)
      return SevenZipFilesInfoReadResult.NotSupported;

    ulong timeBytesU64 = (ulong)definedCount * 8UL;

    // строго: хвост payload — ровно times
    if (timeBytesU64 != (ulong)(payload.Length - offset))
      return SevenZipFilesInfoReadResult.InvalidData;

    ulong[] values = new ulong[fileCount];

    for (int i = 0; i < fileCount; i++)
    {
      if (!def[i])
        continue;

      values[i] = BinaryPrimitives.ReadUInt64LittleEndian(payload.Slice(offset, 8));
      offset += 8;
    }

    if (offset != payload.Length)
      return SevenZipFilesInfoReadResult.InvalidData;

    defined = def;
    times = values;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseWinAttribProperty(
    ReadOnlySpan<byte> payload,
    int fileCount,
    out bool[]? defined,
    out uint[]? attrib)
  {
    defined = null;
    attrib = null;

    // минимум: AllAreDefined + External
    if (payload.Length < 2)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte allAreDefined = payload[0];
    int offset = 1;

    bool[] def = new bool[fileCount];
    int definedCount = 0;

    if (allAreDefined == 1)
    {
      Array.Fill(def, true);
      definedCount = fileCount;
    }
    else if (allAreDefined == 0)
    {
      int definedBytes = (fileCount + 7) / 8;

      // нужно минимум: AllAreDefined + bits + External
      if (payload.Length < 1 + definedBytes + 1)
        return SevenZipFilesInfoReadResult.InvalidData;

      for (int i = 0; i < fileCount; i++)
      {
        byte b = payload[offset + (i >> 3)];
        byte mask = (byte)(0x80 >> (i & 7));
        bool isDef = (b & mask) != 0;
        def[i] = isDef;
        if (isDef)
          definedCount++;
      }

      offset += definedBytes;
    }
    else
      return SevenZipFilesInfoReadResult.InvalidData;

    if (offset >= payload.Length)
      return SevenZipFilesInfoReadResult.InvalidData;

    byte external = payload[offset++];
    if (external != 0)
      return SevenZipFilesInfoReadResult.NotSupported;

    ulong attrBytesU64 = (ulong)definedCount * 4UL;

    // строго: хвост payload — ровно attrs
    if (attrBytesU64 != (ulong)(payload.Length - offset))
      return SevenZipFilesInfoReadResult.InvalidData;

    uint[] values = new uint[fileCount];

    for (int i = 0; i < fileCount; i++)
    {
      if (!def[i])
        continue;

      values[i] = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, 4));
      offset += 4;
    }

    if (offset != payload.Length)
      return SevenZipFilesInfoReadResult.InvalidData;

    defined = def;
    attrib = values;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseEmptyStreamVector(
  ReadOnlySpan<byte> payload,
  int fileCount,
  out bool[]? vector)
  {
    vector = null;

    // kEmptyStream: for(NumFiles) BIT IsEmptyStream
    // payload = ceil(NumFiles/8) байт, без AllAreDefined и без External.
    if (fileCount == 0)
    {
      if (payload.Length != 0)
        return SevenZipFilesInfoReadResult.InvalidData;

      vector = [];
      return SevenZipFilesInfoReadResult.Ok;
    }

    int bytesRequired = (fileCount + 7) / 8;
    if (payload.Length != bytesRequired)
      return SevenZipFilesInfoReadResult.InvalidData;

    bool[] result = new bool[fileCount];

    // Биты MSB -> LSB: 0x80, 0x40, 0x20, ... 0x01
    for (int i = 0; i < fileCount; i++)
    {
      byte b = payload[i >> 3];
      byte mask = (byte)(0x80 >> (i & 7));
      result[i] = (b & mask) != 0;
    }

    vector = result;
    return SevenZipFilesInfoReadResult.Ok;
  }

  private static SevenZipFilesInfoReadResult TryParseEmptyStreamsSubVector(
  ReadOnlySpan<byte> payload,
  bool[]? emptyStreams,
  int fileCount,
  out bool[]? perFile)
  {
    perFile = null;

    int numEmptyStreams = 0;
    if (emptyStreams is not null)
    {
      if (emptyStreams.Length != fileCount)
        return SevenZipFilesInfoReadResult.InvalidData;

      for (int i = 0; i < emptyStreams.Length; i++)
        if (emptyStreams[i])
          numEmptyStreams++;
    }

    int bytesRequired = (numEmptyStreams + 7) / 8;
    if (payload.Length != bytesRequired)
      return SevenZipFilesInfoReadResult.InvalidData;

    bool[] result = new bool[fileCount];

    if (numEmptyStreams == 0)
    {
      perFile = result;
      return SevenZipFilesInfoReadResult.Ok;
    }

    int emptyIndex = 0;

    // kEmptyFile/kAnti: for(EmptyStreams) BIT ...
    // Биты MSB->LSB: 0x80, 0x40, ... 0x01
    for (int i = 0; i < fileCount; i++)
    {
      if (emptyStreams?[i] != true)
        continue;

      int byteIndex = emptyIndex >> 3;
      int bitIndex = emptyIndex & 7;
      byte mask = (byte)(0x80 >> bitIndex);

      result[i] = (payload[byteIndex] & mask) != 0;
      emptyIndex++;
    }

    perFile = result;
    return SevenZipFilesInfoReadResult.Ok;
  }
}
