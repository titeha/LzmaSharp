namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат чтения NextHeader типа Header.
/// </summary>
public enum SevenZipHeaderReadResult
{
  Ok,
  NeedMoreInput,
  InvalidData,
  NotSupported,
}

/// <summary>
/// Читает NextHeader (не EncodedHeader) и собирает минимальную модель заголовка:
/// MainStreamsInfo + FilesInfo.
/// 
/// На этом шаге мы НЕ поддерживаем EncodedHeader, ArchiveProperties и AdditionalStreamsInfo.
/// </summary>
public static class SevenZipHeaderReader
{
  public static SevenZipHeaderReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipHeader header,
    out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    if (input.Length < 1)
      return SevenZipHeaderReadResult.NeedMoreInput;

    int offset = 0;

    byte nid = input[offset];
    if (nid == SevenZipNid.EncodedHeader)
      return SevenZipHeaderReadResult.NotSupported;

    if (nid != SevenZipNid.Header)
      return SevenZipHeaderReadResult.InvalidData;

    offset++;

    SevenZipStreamsInfo streamsInfo = default!;
    SevenZipFilesInfo filesInfo = default;

    bool streamsInfoRead = false;
    bool filesInfoRead = false;

    while (true)
    {
      if (offset >= input.Length)
        return SevenZipHeaderReadResult.NeedMoreInput;

      byte id = input[offset];

      if (id == SevenZipNid.End)
      {
        offset++;
        header = new SevenZipHeader(streamsInfo, filesInfo);
        bytesConsumed = offset;
        return SevenZipHeaderReadResult.Ok;
      }

      if (id == SevenZipNid.MainStreamsInfo)
      {
        if (streamsInfoRead)
          return SevenZipHeaderReadResult.InvalidData;

        offset++; // пропускаем NID.MainStreamsInfo

        var res = SevenZipStreamsInfoReader.TryRead(input.Slice(offset), out streamsInfo, out int consumed);
        if (res == SevenZipStreamsInfoReadResult.NeedMoreInput)
          return SevenZipHeaderReadResult.NeedMoreInput;
        if (res == SevenZipStreamsInfoReadResult.InvalidData)
          return SevenZipHeaderReadResult.InvalidData;
        if (res == SevenZipStreamsInfoReadResult.NotSupported)
          return SevenZipHeaderReadResult.NotSupported;

        offset += consumed;
        streamsInfoRead = true;
        continue;
      }

      if (id == SevenZipNid.FilesInfo)
      {
        if (filesInfoRead)
          return SevenZipHeaderReadResult.InvalidData;

        var res = SevenZipFilesInfoReader.TryRead(input.Slice(offset), out filesInfo, out int consumed);
        if (res == SevenZipFilesInfoReadResult.NeedMoreInput)
          return SevenZipHeaderReadResult.NeedMoreInput;
        if (res == SevenZipFilesInfoReadResult.InvalidData)
          return SevenZipHeaderReadResult.InvalidData;
        if (res == SevenZipFilesInfoReadResult.NotSupported)
          return SevenZipHeaderReadResult.NotSupported;

        offset += consumed;
        filesInfoRead = true;
        continue;
      }

      // Архивные свойства / дополнительные потоки и пр. пока не реализуем.
      return SevenZipHeaderReadResult.NotSupported;
    }
  }
}
