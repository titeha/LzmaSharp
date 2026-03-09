using System.Buffers.Binary;

namespace Lzma.Core.SevenZip;

public enum SevenZipPackInfoReadResult
{
  Ok = 0,
  NeedMoreInput = 1,
  InvalidData = 2,
  NotSupported = 3,
}

/// <summary>
/// Читает структуру PackInfo.
/// Формат: PackInfo ::= kPackInfo packPos numPackStreams [kSize sizes...] [kCRC ...] kEnd
/// </summary>
public static class SevenZipPackInfoReader
{
  public static SevenZipPackInfoReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipPackInfo packInfo,
    out int bytesConsumed)
  {
    packInfo = default;
    bytesConsumed = 0;

    bool[]? crcDefined = null;
    uint[]? crc = null;

    // Парсим атомарно: если данных не хватает, не двигаем bytesConsumed.
    int cursor = 0;
    if (input.Length == 0)
      return SevenZipPackInfoReadResult.NeedMoreInput;

    if (input[cursor] != SevenZipNid.PackInfo)
      return SevenZipPackInfoReadResult.InvalidData;
    cursor++;

    SevenZipEncodedUInt64.ReadResult rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong packPos, out int br);
    if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
      return SevenZipPackInfoReadResult.NeedMoreInput;
    if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
      return SevenZipPackInfoReadResult.InvalidData;
    cursor += br;

    rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong numPackStreamsU64, out br);
    if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
      return SevenZipPackInfoReadResult.NeedMoreInput;
    if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
      return SevenZipPackInfoReadResult.InvalidData;
    cursor += br;

    if (numPackStreamsU64 > int.MaxValue)
      return SevenZipPackInfoReadResult.NotSupported;
    int numPackStreams = (int)numPackStreamsU64;

    bool haveSizes = false;
    ulong[] sizes = [];
    bool haveCrc = false;

    while (true)
    {
      if (cursor >= input.Length)
        return SevenZipPackInfoReadResult.NeedMoreInput;

      byte nid = input[cursor++];
      if (nid == SevenZipNid.End)
      {
        if (!haveSizes)
          return SevenZipPackInfoReadResult.InvalidData;

        packInfo = new SevenZipPackInfo(packPos, sizes, crcDefined, crc);
        bytesConsumed = cursor;
        return SevenZipPackInfoReadResult.Ok;
      }

      if (nid == SevenZipNid.Size)
      {
        if (haveSizes) // Повторный блок Size не ожидаем.
          return SevenZipPackInfoReadResult.InvalidData;

        sizes = new ulong[numPackStreams];
        for (int i = 0; i < numPackStreams; i++)
        {
          rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong size, out br);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            return SevenZipPackInfoReadResult.NeedMoreInput;
          if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
            return SevenZipPackInfoReadResult.InvalidData;
          cursor += br;
          sizes[i] = size;
        }

        haveSizes = true;
        continue;
      }

      if (nid == SevenZipNid.Crc)
      {
        // kCRC в PackInfo кодируется как Digests(NumPackStreams):
        // BYTE AllAreDefined; если 0 => далее BIT-вектор Defined[NumStreams]; затем CRCs[NumDefined] (UINT32).

        if (!haveSizes)
          return SevenZipPackInfoReadResult.InvalidData;

        if (haveCrc)
          return SevenZipPackInfoReadResult.InvalidData;

        haveCrc = true;

        if (cursor >= input.Length)
          return SevenZipPackInfoReadResult.NeedMoreInput;

        byte allAreDefined = input[cursor++];

        if (allAreDefined == 1)
        {
          // CRC задан для всех потоков.
          ulong needed = (ulong)numPackStreams * 4UL;
          if (needed > (ulong)(input.Length - cursor))
            return SevenZipPackInfoReadResult.NeedMoreInput;

          crcDefined = new bool[numPackStreams];
          crc = new uint[numPackStreams];

          for (int i = 0; i < numPackStreams; i++)
          {
            crcDefined[i] = true;
            crc[i] = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(cursor, 4));
            cursor += 4;
          }

          continue;
        }

        if (allAreDefined == 0)
        {
          int definedBytes = (numPackStreams + 7) / 8;
          if (input.Length - cursor < definedBytes)
            return SevenZipPackInfoReadResult.NeedMoreInput;

          // Сначала считаем количество defined, чтобы проверить, хватит ли байт под CRC.
          int definedCount = 0;
          for (int i = 0; i < numPackStreams; i++)
          {
            byte b = input[cursor + (i >> 3)];
            byte mask = (byte)(0x80 >> (i & 7));
            if ((b & mask) != 0)
              definedCount++;
          }

          ulong needed = (ulong)definedCount * 4UL;
          if (needed > (ulong)(input.Length - cursor - definedBytes))
            return SevenZipPackInfoReadResult.NeedMoreInput;

          crcDefined = new bool[numPackStreams];
          crc = new uint[numPackStreams];

          // Заполняем defined
          for (int i = 0; i < numPackStreams; i++)
          {
            byte b = input[cursor + (i >> 3)];
            byte mask = (byte)(0x80 >> (i & 7));
            crcDefined[i] = (b & mask) != 0;
          }

          cursor += definedBytes;

          // Читаем CRC только для defined
          for (int i = 0; i < numPackStreams; i++)
          {
            if (!crcDefined[i])
              continue;

            crc[i] = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(cursor, 4));
            cursor += 4;
          }

          continue;
        }

        return SevenZipPackInfoReadResult.InvalidData;
      }

      return SevenZipPackInfoReadResult.InvalidData;
    }
  }
}
