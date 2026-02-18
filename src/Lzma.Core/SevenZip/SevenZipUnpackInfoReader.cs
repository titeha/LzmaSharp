namespace Lzma.Core.SevenZip;

public enum SevenZipUnpackInfoReadResult
{
  Ok = 0,
  NeedMoreInput = 1,
  InvalidData = 2,
  NotSupported = 3,
}

/// <summary>
/// Читает UnpackInfo (в терминологии 7zFormat.txt это «CodersInfo»).
/// Формат (упрощённо):
/// UnpackInfo ::= kUnpackInfo FolderInfo kCodersUnpackSize UnpackSizes [kCRC ...] kEnd
/// FolderInfo ::= kFolder NumFolders External (Folders...)
/// </summary>
public static class SevenZipUnpackInfoReader
{
  public static SevenZipUnpackInfoReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipUnpackInfo unpackInfo,
    out int bytesConsumed)
  {
    unpackInfo = default!;
    bytesConsumed = 0;

    // Парсим атомарно: если данных не хватает, не двигаем bytesConsumed.
    int cursor = 0;

    if (input.Length == 0)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;

    if (input[cursor] != SevenZipNid.UnpackInfo)
      return SevenZipUnpackInfoReadResult.InvalidData;
    cursor++;

    // Ожидаем kFolder
    if (cursor >= input.Length)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;
    if (input[cursor] != SevenZipNid.Folder)
      return SevenZipUnpackInfoReadResult.InvalidData;
    cursor++;

    // NumFolders
    SevenZipEncodedUInt64.ReadResult rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong numFoldersU64, out int br);
    if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;
    if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
      return SevenZipUnpackInfoReadResult.InvalidData;
    cursor += br;

    if (numFoldersU64 > int.MaxValue)
      return SevenZipUnpackInfoReadResult.NotSupported;
    int numFolders = (int)numFoldersU64;

    // External (0/1). На данном шаге поддерживаем только External == 0.
    if (cursor >= input.Length)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;
    byte external = input[cursor++];
    if (external != 0)
      return SevenZipUnpackInfoReadResult.NotSupported;

    SevenZipFolder[] folders = new SevenZipFolder[numFolders];

    // Folders...
    for (int f = 0; f < numFolders; f++)
    {
      rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong numCodersU64, out br);
      if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
        return SevenZipUnpackInfoReadResult.NeedMoreInput;
      if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
        return SevenZipUnpackInfoReadResult.InvalidData;
      cursor += br;

      if (numCodersU64 == 0)
        return SevenZipUnpackInfoReadResult.InvalidData;
      if (numCodersU64 > int.MaxValue)
        return SevenZipUnpackInfoReadResult.NotSupported;
      int numCoders = (int)numCodersU64;

      SevenZipCoderInfo[] coders = new SevenZipCoderInfo[numCoders];

      ulong totalInStreams = 0;
      ulong totalOutStreams = 0;

      for (int c = 0; c < numCoders; c++)
      {
        if (cursor >= input.Length)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;

        byte mainByte = input[cursor++];

        // В текущей реализации длина MethodID хранится прямо в младших 4 битах (1..15).
        // Ноль считаем некорректным значением.
        int codecIdSize = mainByte & 0x0F;
        if (codecIdSize == 0)
          return SevenZipUnpackInfoReadResult.InvalidData;

        bool isComplexCoder = (mainByte & 0x10) != 0;
        bool hasAttributes = (mainByte & 0x20) != 0;

        // bit6 зарезервирован, bit7 «alternative methods» (в 7zFormat.txt сказано что не используется и должен быть 0).
        if ((mainByte & 0x40) != 0)
          return SevenZipUnpackInfoReadResult.InvalidData;
        if ((mainByte & 0x80) != 0)
          return SevenZipUnpackInfoReadResult.InvalidData;

        if (input.Length - cursor < codecIdSize)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;

        byte[] methodId = input.Slice(cursor, codecIdSize).ToArray();
        cursor += codecIdSize;

        ulong numInStreams = 1;
        ulong numOutStreams = 1;

        if (isComplexCoder)
        {
          rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out numInStreams, out br);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            return SevenZipUnpackInfoReadResult.NeedMoreInput;
          if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
            return SevenZipUnpackInfoReadResult.InvalidData;
          cursor += br;

          rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out numOutStreams, out br);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            return SevenZipUnpackInfoReadResult.NeedMoreInput;
          if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
            return SevenZipUnpackInfoReadResult.InvalidData;
          cursor += br;

          if (numInStreams == 0 || numOutStreams == 0)
            return SevenZipUnpackInfoReadResult.InvalidData;
        }

        byte[] properties = [];
        if (hasAttributes)
        {
          rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong propsSizeU64, out br);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            return SevenZipUnpackInfoReadResult.NeedMoreInput;
          if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
            return SevenZipUnpackInfoReadResult.InvalidData;
          cursor += br;

          if (propsSizeU64 > int.MaxValue)
            return SevenZipUnpackInfoReadResult.NotSupported;
          int propsSize = (int)propsSizeU64;

          if (input.Length - cursor < propsSize)
            return SevenZipUnpackInfoReadResult.NeedMoreInput;

          properties = input.Slice(cursor, propsSize).ToArray();
          cursor += propsSize;
        }

        // Пока что мы не поддерживаем «многопоточные» кодеры, потому что нам нужно будет правильно
        // интерпретировать BindPairs/PackedStreamIndices.
        // Но уже сейчас считаем totals — они нужны для дальнейшего парсинга Folder.
        totalInStreams += numInStreams;
        totalOutStreams += numOutStreams;

        coders[c] = new SevenZipCoderInfo(methodId, properties, numInStreams, numOutStreams);
      }

      if (totalOutStreams == 0)
        return SevenZipUnpackInfoReadResult.InvalidData;

      // BindPairs: NumBindPairs = TotalOutStreams - 1
      ulong numBindPairsU64 = totalOutStreams - 1;
      if (numBindPairsU64 > int.MaxValue)
        return SevenZipUnpackInfoReadResult.NotSupported;
      int numBindPairs = (int)numBindPairsU64;

      SevenZipBindPair[] bindPairs = new SevenZipBindPair[numBindPairs];
      for (int i = 0; i < numBindPairs; i++)
      {
        rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong inIndex, out br);
        if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;
        if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
          return SevenZipUnpackInfoReadResult.InvalidData;
        cursor += br;

        rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong outIndex, out br);
        if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;
        if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
          return SevenZipUnpackInfoReadResult.InvalidData;
        cursor += br;

        bindPairs[i] = new SevenZipBindPair(inIndex, outIndex);
      }

      // NumPackedStreams = TotalInStreams - NumBindPairs
      if (totalInStreams < numBindPairsU64)
        return SevenZipUnpackInfoReadResult.InvalidData;
      ulong numPackedStreamsU64 = totalInStreams - numBindPairsU64;
      if (numPackedStreamsU64 == 0)
        return SevenZipUnpackInfoReadResult.InvalidData;
      if (numPackedStreamsU64 > int.MaxValue)
        return SevenZipUnpackInfoReadResult.NotSupported;
      int numPackedStreams = (int)numPackedStreamsU64;

      // Если PackedStreams == 1, индекс считается равным 0 и НЕ хранится в потоке.
      ulong[] packedStreamIndices;
      if (numPackedStreams == 1)
        packedStreamIndices = [0];
      else
      {
        packedStreamIndices = new ulong[numPackedStreams];
        for (int i = 0; i < numPackedStreams; i++)
        {
          rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong packedIndex, out br);
          if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
            return SevenZipUnpackInfoReadResult.NeedMoreInput;
          if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
            return SevenZipUnpackInfoReadResult.InvalidData;
          cursor += br;
          packedStreamIndices[i] = packedIndex;
        }
      }

      folders[f] = new SevenZipFolder(
        Coders: coders,
        BindPairs: bindPairs,
        PackedStreamIndices: packedStreamIndices,
        NumInStreams: totalInStreams,
        NumOutStreams: totalOutStreams);
    }

    // Ожидаем kCodersUnpackSize
    if (cursor >= input.Length)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;
    if (input[cursor] != SevenZipNid.CodersUnpackSize)
      return SevenZipUnpackInfoReadResult.InvalidData;
    cursor++;

    // UnpackSizes: для каждого folder читаем столько значений, сколько у него NumOutStreams.
    ulong[][] folderUnpackSizes = new ulong[numFolders][];
    for (int f = 0; f < numFolders; f++)
    {
      ulong outCountU64 = folders[f].NumOutStreams;
      if (outCountU64 > int.MaxValue)
        return SevenZipUnpackInfoReadResult.NotSupported;
      int outCount = (int)outCountU64;

      ulong[] sizes = new ulong[outCount];
      for (int i = 0; i < outCount; i++)
      {
        rr = SevenZipEncodedUInt64.TryRead(input[cursor..], out ulong size, out br);
        if (rr == SevenZipEncodedUInt64.ReadResult.NeedMoreInput)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;
        if (rr != SevenZipEncodedUInt64.ReadResult.Ok)
          return SevenZipUnpackInfoReadResult.InvalidData;
        cursor += br;
        sizes[i] = size;
      }

      folderUnpackSizes[f] = sizes;
    }

    // Дальше либо kCRC (UnPackDigests[NumFolders]), либо kEnd.
    if (cursor >= input.Length)
      return SevenZipUnpackInfoReadResult.NeedMoreInput;

    byte nidAfterSizes = input[cursor++];

    if (nidAfterSizes == SevenZipNid.Crc)
    {
      // Digests(NumFolders):
      // BYTE AllAreDefined
      // if (AllAreDefined == 0) { for(NumFolders) BIT Defined }
      // UINT32 CRCs[NumDefined]

      if (cursor >= input.Length)
        return SevenZipUnpackInfoReadResult.NeedMoreInput;

      byte allAreDefined = input[cursor++];

      bool[] folderCrcDefined = new bool[numFolders];
      int definedCount;

      if (allAreDefined == 1)
      {
        for (int i = 0; i < numFolders; i++)
          folderCrcDefined[i] = true;

        definedCount = numFolders;
      }
      else if (allAreDefined == 0)
      {
        int definedBytes = (numFolders + 7) / 8;
        if (input.Length - cursor < definedBytes)
          return SevenZipUnpackInfoReadResult.NeedMoreInput;

        definedCount = 0;

        // Биты MSB->LSB: 0x80, 0x40, ... 0x01
        for (int i = 0; i < numFolders; i++)
        {
          byte b = input[cursor + (i >> 3)];
          byte mask = (byte)(0x80 >> (i & 7));
          bool isDefined = (b & mask) != 0;

          folderCrcDefined[i] = isDefined;
          if (isDefined)
            definedCount++;
        }

        cursor += definedBytes;
      }
      else
        return SevenZipUnpackInfoReadResult.InvalidData;

      ulong crcBytesU64 = (ulong)definedCount * 4UL;
      if (crcBytesU64 > (ulong)(input.Length - cursor))
        return SevenZipUnpackInfoReadResult.NeedMoreInput;

      cursor += (int)crcBytesU64;

      if (cursor >= input.Length)
        return SevenZipUnpackInfoReadResult.NeedMoreInput;

      byte endAfterCrc = input[cursor++];
      if (endAfterCrc != SevenZipNid.End)
        return SevenZipUnpackInfoReadResult.InvalidData;

      // ВАЖНО: теперь передаём folderCrcDefined.
      unpackInfo = new SevenZipUnpackInfo(folders, folderUnpackSizes, folderCrcDefined);
      bytesConsumed = cursor;
      return SevenZipUnpackInfoReadResult.Ok;
    }

    if (nidAfterSizes != SevenZipNid.End)
      return SevenZipUnpackInfoReadResult.InvalidData;

    unpackInfo = new SevenZipUnpackInfo(folders, folderUnpackSizes);
    bytesConsumed = cursor;
    return SevenZipUnpackInfoReadResult.Ok;
  }
}
