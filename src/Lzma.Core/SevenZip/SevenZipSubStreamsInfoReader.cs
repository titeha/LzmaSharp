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
        // На этом шаге CRC для sub-streams пока не поддерживаем.
        subStreamsInfo = null;
        bytesConsumed = 0;
        return SevenZipSubStreamsInfoReadResult.NotSupported;
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
