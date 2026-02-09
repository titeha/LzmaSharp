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

    while (true)
    {
      if (cursor >= input.Length)
        return SevenZipPackInfoReadResult.NeedMoreInput;

      byte nid = input[cursor++];
      if (nid == SevenZipNid.End)
      {
        if (!haveSizes)
          return SevenZipPackInfoReadResult.InvalidData;

        packInfo = new SevenZipPackInfo(packPos, sizes);
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

      if (nid == SevenZipNid.Crc) // CRC пока не нужен в наших шагах; добавим поддержку позже.
        return SevenZipPackInfoReadResult.NotSupported;

      return SevenZipPackInfoReadResult.InvalidData;
    }
  }
}
