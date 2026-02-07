namespace Lzma.Core.SevenZip;

public enum SevenZipStreamsInfoReadResult
{
  Ok,
  NeedMoreInput,
  InvalidData,
  NotSupported,
}

public static class SevenZipStreamsInfoReader
{
  public static SevenZipStreamsInfoReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipStreamsInfo streamsInfo,
    out int bytesConsumed)
  {
    // NOTE: по текущей договорённости с тестами, при NeedMoreInput/ошибке
    // bytesConsumed = 0 (чтобы вызывающий код мог просто дополнить вход).
    streamsInfo = null!;
    bytesConsumed = 0;

    SevenZipPackInfo? packInfo = null;
    SevenZipUnpackInfo? unpackInfo = null;
    SevenZipSubStreamsInfo? subStreamsInfo = null;

    int offset = 0;
    while (true)
    {
      if (offset >= input.Length)
        return SevenZipStreamsInfoReadResult.NeedMoreInput;

      byte nid = input[offset];
      if (nid == SevenZipNid.End)
      {
        offset++;
        streamsInfo = new SevenZipStreamsInfo(packInfo, unpackInfo, subStreamsInfo);
        bytesConsumed = offset;
        return SevenZipStreamsInfoReadResult.Ok;
      }

      switch (nid)
      {
        case SevenZipNid.PackInfo:
        {
          // Строгий порядок: PackInfo должен быть первым (если есть).
          if (packInfo is not null || unpackInfo is not null || subStreamsInfo is not null)
            return SevenZipStreamsInfoReadResult.InvalidData;

          switch (SevenZipPackInfoReader.TryRead(input[offset..], out var tmpPackInfo, out int consumed))
          {
            case SevenZipPackInfoReadResult.Ok:
              packInfo = tmpPackInfo;
              offset += consumed;
              break;
            case SevenZipPackInfoReadResult.NeedMoreInput:
              return SevenZipStreamsInfoReadResult.NeedMoreInput;
            case SevenZipPackInfoReadResult.InvalidData:
              return SevenZipStreamsInfoReadResult.InvalidData;
            case SevenZipPackInfoReadResult.NotSupported:
              return SevenZipStreamsInfoReadResult.NotSupported;
            default:
              return SevenZipStreamsInfoReadResult.InvalidData;
          }

          break;
        }

        case SevenZipNid.UnpackInfo:
        {
          // Строгий порядок: UnpackInfo не должен идти до PackInfo.
          if (unpackInfo is not null || subStreamsInfo is not null || packInfo is null)
            return SevenZipStreamsInfoReadResult.InvalidData;

          switch (SevenZipUnpackInfoReader.TryRead(input[offset..], out var tmpUnpackInfo, out int consumed))
          {
            case SevenZipUnpackInfoReadResult.Ok:
              unpackInfo = tmpUnpackInfo;
              offset += consumed;
              break;
            case SevenZipUnpackInfoReadResult.NeedMoreInput:
              return SevenZipStreamsInfoReadResult.NeedMoreInput;
            case SevenZipUnpackInfoReadResult.InvalidData:
              return SevenZipStreamsInfoReadResult.InvalidData;
            case SevenZipUnpackInfoReadResult.NotSupported:
              return SevenZipStreamsInfoReadResult.NotSupported;
            default:
              return SevenZipStreamsInfoReadResult.InvalidData;
          }

          break;
        }

        case SevenZipNid.SubStreamsInfo:
        {
          // Строгий порядок: SubStreamsInfo возможен только после UnpackInfo.
          if (subStreamsInfo is not null || unpackInfo is null)
            return SevenZipStreamsInfoReadResult.InvalidData;

          switch (SevenZipSubStreamsInfoReader.TryRead(input[offset..], unpackInfo, out var tmpSub, out int consumed))
          {
            case SevenZipSubStreamsInfoReadResult.Ok:
              subStreamsInfo = tmpSub;
              offset += consumed;
              break;
            case SevenZipSubStreamsInfoReadResult.NeedMoreInput:
              return SevenZipStreamsInfoReadResult.NeedMoreInput;
            case SevenZipSubStreamsInfoReadResult.InvalidData:
              return SevenZipStreamsInfoReadResult.InvalidData;
            case SevenZipSubStreamsInfoReadResult.NotSupported:
              return SevenZipStreamsInfoReadResult.NotSupported;
            default:
              return SevenZipStreamsInfoReadResult.InvalidData;
          }

          break;
        }

        default:
          return SevenZipStreamsInfoReadResult.InvalidData;
      }
    }
  }
}
