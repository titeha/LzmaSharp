namespace Lzma.Core.SevenZip;

public enum SevenZipArchiveDecodeResult
{
  Ok,
  NeedMoreData,
  InvalidData,
  NotSupported,
}

public static class SevenZipArchiveDecoder
{
  /// <summary>
  /// Декодирует 7z-архив, который содержит ровно один файл, в массив байт.
  ///
  /// Ограничения текущей реализации:
  /// - в архиве ровно один файл;
  /// - ровно один folder в StreamsInfo.UnpackInfo;
  /// - пока поддерживается только простая схема: данные файла = распакованные данные folder[0].
  /// </summary>
  public static SevenZipArchiveDecodeResult DecodeSingleFileToArray(
      ReadOnlySpan<byte> archive,
      out byte[] output,
      out string? fileName,
      out int bytesConsumed)
  {
    output = [];
    fileName = null;

    var reader = new SevenZipArchiveReader();
    switch (reader.Read(archive, out bytesConsumed))
    {
      case SevenZipArchiveReadResult.Ok:
        break;
      case SevenZipArchiveReadResult.NeedMoreInput:
        return SevenZipArchiveDecodeResult.NeedMoreData;
      case SevenZipArchiveReadResult.InvalidData:
        return SevenZipArchiveDecodeResult.InvalidData;
      case SevenZipArchiveReadResult.NotSupported:
        return SevenZipArchiveDecodeResult.NotSupported;
      default:
        return SevenZipArchiveDecodeResult.InvalidData;
    }

    if (!reader.Header.HasValue)
      return SevenZipArchiveDecodeResult.InvalidData;

    SevenZipHeader header = reader.Header.Value;

    // Пока реализуем только самый простой вариант: один файл.
    if (header.FilesInfo.FileCount != 1)
      return SevenZipArchiveDecodeResult.NotSupported;

    if (header.FilesInfo.Names!.Length == 1)
      fileName = header.FilesInfo.Names[0];

    if (header.StreamsInfo is null) // Без StreamsInfo мы не знаем, где и как лежат данные.
      return SevenZipArchiveDecodeResult.NotSupported;

    SevenZipStreamsInfo streamsInfo = header.StreamsInfo!;

    if (streamsInfo.PackInfo is not { } || streamsInfo.UnpackInfo is null)
      return SevenZipArchiveDecodeResult.NotSupported;

    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    // На текущем шаге поддерживаем только один folder.
    if (unpackInfo.Folders.Length != 1)
      return SevenZipArchiveDecodeResult.NotSupported;

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult folderResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams,
        folderIndex: 0,
        out output);

    return folderResult switch
    {
      SevenZipFolderDecodeResult.Ok => SevenZipArchiveDecodeResult.Ok,
      SevenZipFolderDecodeResult.NotSupported => SevenZipArchiveDecodeResult.NotSupported,
      _ => SevenZipArchiveDecodeResult.InvalidData,
    };
  }
}
