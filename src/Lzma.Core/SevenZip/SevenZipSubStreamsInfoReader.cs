namespace Lzma.Core.SevenZip;

public enum SevenZipSubStreamsInfoReadResult
{
  Ok = 0,
  NeedMoreInput = 1,
  InvalidData = 2,
  NotSupported = 3,
}

public static class SevenZipSubStreamsInfoReader
{
  public static SevenZipSubStreamsInfoReadResult TryRead(
    ReadOnlySpan<byte> src,
    SevenZipUnpackInfo unpackInfo,
    out SevenZipSubStreamsInfo? subStreamsInfo,
    out int bytesConsumed)
  {
    if (src.IsEmpty)
    {
      subStreamsInfo = null;
      bytesConsumed = 0;
      return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
    }

    if (src[0] != SevenZipNid.SubStreamsInfo)
    {
      subStreamsInfo = null;
      bytesConsumed = 0;
      return SevenZipSubStreamsInfoReadResult.InvalidData;
    }

    int offset = 1;

    int folderCount = unpackInfo.Folders.Length;
    if (folderCount == 0)
    {
      subStreamsInfo = null;
      bytesConsumed = 0;
      return SevenZipSubStreamsInfoReadResult.InvalidData;
    }

    // По умолчанию на каждую папку приходится ровно один unpack stream.
    var numUnpackStreamsPerFolder = new ulong[folderCount];
    for (int i = 0; i < folderCount; i++)
      numUnpackStreamsPerFolder[i] = 1;

    // Если секции Size нет, то (при num==1) размер берём из UnpackInfo.
    ulong[][]? unpackSizesPerFolder = null;

    bool seenNumUnpackStream = false;
    bool seenSize = false;

    while (true)
    {
      if (offset >= src.Length)
      {
        subStreamsInfo = null;
        bytesConsumed = 0;
        return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
      }

      byte nid = src[offset++];

      if (nid == SevenZipNid.End)
      {
        // Финализация.
        if (!seenSize)
        {
          // Если указано num>1, но нет Size, мы пока не умеем корректно восстановить размеры.
          for (int f = 0; f < folderCount; f++)
            if (numUnpackStreamsPerFolder[f] != 1)
            {
              subStreamsInfo = null;
              bytesConsumed = 0;
              return SevenZipSubStreamsInfoReadResult.NotSupported;
            }

          unpackSizesPerFolder = new ulong[folderCount][];
          for (int f = 0; f < folderCount; f++)
            unpackSizesPerFolder[f] = [GetFolderTotalUnpackSize(unpackInfo, f)];
        }
        else if (unpackSizesPerFolder is null)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.InvalidData;
        }

        subStreamsInfo = new SevenZipSubStreamsInfo(numUnpackStreamsPerFolder, unpackSizesPerFolder);
        bytesConsumed = offset;
        return SevenZipSubStreamsInfoReadResult.Ok;
      }

      if (nid == SevenZipNid.NumUnpackStream)
      {
        if (seenNumUnpackStream)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.InvalidData;
        }

        seenNumUnpackStream = true;

        for (int f = 0; f < folderCount; f++)
        {
          var rr = SevenZipEncodedUInt64.TryRead(src[offset..], out var value, out int consumed);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
          }
          // В текущей реализации SevenZipEncodedUInt64.TryRead() не имеет варианта "InvalidData":
          // любая последовательность байт имеет корректную длину (1..9), а недостаток данных
          // возвращается как NeedMoreInput.

          offset += consumed;

          ulong n = value;
          if (n == 0)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.InvalidData;
          }

          // На всякий случай ограничим размер для аллокаций.
          if (n > int.MaxValue)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.NotSupported;
          }

          numUnpackStreamsPerFolder[f] = n;
        }

        continue;
      }

      if (nid == SevenZipNid.Size)
      {
        if (seenSize)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.InvalidData;
        }

        seenSize = true;

        unpackSizesPerFolder = new ulong[folderCount][];

        for (int f = 0; f < folderCount; f++)
        {
          ulong n = numUnpackStreamsPerFolder[f];
          if (n == 0)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.InvalidData;
          }

          if (n > int.MaxValue)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.NotSupported;
          }

          ulong folderTotal = GetFolderTotalUnpackSize(unpackInfo, f);

          int streams = (int)n;
          var sizes = new ulong[streams];

          if (streams == 1)
          {
            sizes[0] = folderTotal;
            unpackSizesPerFolder[f] = sizes;
            continue;
          }

          ulong sum = 0;
          for (int i = 0; i < streams - 1; i++)
          {
            var rr = SevenZipEncodedUInt64.TryRead(src[offset..], out var value, out int consumed);
            if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            {
              subStreamsInfo = null;
              bytesConsumed = 0;
              return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
            }
            // См. комментарий выше: InvalidData у SevenZipEncodedUInt64.TryRead() сейчас отсутствует.

            offset += consumed;

            ulong size = value;
            sum += size;

            if (sum > folderTotal)
            {
              subStreamsInfo = null;
              bytesConsumed = 0;
              return SevenZipSubStreamsInfoReadResult.InvalidData;
            }

            sizes[i] = size;
          }

          sizes[streams - 1] = folderTotal - sum;
          unpackSizesPerFolder[f] = sizes;
        }

        continue;
      }

      if (nid == SevenZipNid.Crc)
      {
        // kCRC в SubStreamsInfo: Digests(NumStreams)
        // BYTE AllAreDefined
        // if (AllAreDefined == 0) { for(NumStreams) BIT Defined }
        // UINT32 CRCs[NumDefined]
        //
        // На этом этапе мы CRC не используем, но должны корректно пропустить секцию.
        // В нашей текущей модели считаем NumStreams = sum(NumUnpackStreamsPerFolder).
        // (Более точный расчёт зависит от наличия CRC на уровне folder в UnpackInfo.)
        // kCRC в SubStreamsInfo: Digests для "количества потоков с неизвестным CRC".
        // Если у folder ровно один sub-stream и CRC задан на уровне folder (UnpackInfo.kCRC),
        // то для этого sub-stream CRC в SubStreamsInfo не ожидается.

        bool[]? folderCrcDefined = unpackInfo.FolderCrcDefined;
        if (folderCrcDefined is not null && folderCrcDefined.Length != folderCount)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.InvalidData;
        }

        ulong numStreamsU64 = 0;

        for (int f = 0; f < folderCount; f++)
        {
          ulong n = numUnpackStreamsPerFolder[f];
          if (n == 0)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.InvalidData;
          }

          bool hasFolderCrc = folderCrcDefined is not null && folderCrcDefined[f];

          // 1 stream + CRC на уровне folder => unknown CRC streams = 0
          if (n == 1 && hasFolderCrc)
            continue;

          numStreamsU64 += n;
        }

        if (numStreamsU64 > int.MaxValue)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.NotSupported;
        }

        int numStreams = (int)numStreamsU64;

        if (offset >= src.Length)
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
        }

        byte allAreDefined = src[offset++];

        int definedCount;

        if (allAreDefined == 1)
          definedCount = numStreams;
        else if (allAreDefined == 0)
        {
          int definedBytes = (numStreams + 7) / 8;
          if (src.Length - offset < definedBytes)
          {
            subStreamsInfo = null;
            bytesConsumed = 0;
            return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
          }

          definedCount = 0;

          // Биты MSB->LSB: 0x80, 0x40, ... 0x01
          for (int i = 0; i < numStreams; i++)
          {
            byte b = src[offset + (i >> 3)];
            byte mask = (byte)(0x80 >> (i & 7));
            if ((b & mask) != 0)
              definedCount++;
          }

          offset += definedBytes;
        }
        else
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.InvalidData;
        }

        ulong crcBytesU64 = (ulong)definedCount * 4UL;
        if (crcBytesU64 > (ulong)(src.Length - offset))
        {
          subStreamsInfo = null;
          bytesConsumed = 0;
          return SevenZipSubStreamsInfoReadResult.NeedMoreInput;
        }

        offset += (int)crcBytesU64;
        continue;
      }

      // Неожиданный/неподдерживаемый элемент.
      subStreamsInfo = null;
      bytesConsumed = 0;
      return SevenZipSubStreamsInfoReadResult.InvalidData;
    }
  }

  private static ulong GetFolderTotalUnpackSize(SevenZipUnpackInfo unpackInfo, int folderIndex)
  {
    // В большинстве практических случаев (и в наших тестах) у папки один выходной поток.
    // Если потоков несколько, то на этом этапе мы считаем, что интересен первый.
    var sizes = unpackInfo.FolderUnpackSizes[folderIndex];
    if (sizes.Length == 0)
      throw new InvalidOperationException("FolderUnpackSizes не содержит ни одного элемента.");

    return sizes[0];
  }
}
