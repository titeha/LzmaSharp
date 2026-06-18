namespace Lzma.Core.SevenZip;

internal static class SevenZipEncodedHeaderDecoder
{
  public static SevenZipArchiveReadResult TryDecode(
    ReadOnlySpan<byte> nextHeaderBytes,
    ReadOnlySpan<byte> packedStreams,
    out byte[] decodedHeaderBytes,
    out SevenZipHeader decodedHeader)
  {
    return TryDecode(
        nextHeaderBytes: nextHeaderBytes,
        packedStreams: packedStreams,
        options: SevenZipDecodeOptions.Default,
        decodedHeaderBytes: out decodedHeaderBytes,
        decodedHeader: out decodedHeader);
  }

  public static SevenZipArchiveReadResult TryDecode(
    ReadOnlySpan<byte> nextHeaderBytes,
    ReadOnlySpan<byte> packedStreams,
    SevenZipDecodeOptions options,
    out byte[] decodedHeaderBytes,
    out SevenZipHeader decodedHeader)
  {
    ArgumentNullException.ThrowIfNull(options);

    decodedHeaderBytes = [];
    decodedHeader = default;

    if (nextHeaderBytes.IsEmpty)
      return SevenZipArchiveReadResult.InvalidData;

    if (nextHeaderBytes[0] != SevenZipNid.EncodedHeader)
      return SevenZipArchiveReadResult.InvalidData;

    const int offset = 1;

    var streamsInfoRead = SevenZipStreamsInfoReader.TryRead(
        nextHeaderBytes[offset..],
        out SevenZipStreamsInfo streamsInfo,
        out _);

    switch (streamsInfoRead)
    {
      case SevenZipStreamsInfoReadResult.Ok:
        break;
      case SevenZipStreamsInfoReadResult.NeedMoreInput:
        return SevenZipArchiveReadResult.NeedMoreInput;
      case SevenZipStreamsInfoReadResult.NotSupported:
        return SevenZipArchiveReadResult.NotSupported;
      default:
        return SevenZipArchiveReadResult.InvalidData;
    }

    if (streamsInfo.PackInfo is null || streamsInfo.UnpackInfo is null)
      return SevenZipArchiveReadResult.InvalidData;

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo;

    if (packInfo.PackSizes.Length == 0)
      return SevenZipArchiveReadResult.InvalidData;

    if (packInfo.PackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    ulong packPos = packInfo.PackPos;
    ulong packSize = packInfo.PackSizes[0];

    if (packPos > (ulong)packedStreams.Length)
      return SevenZipArchiveReadResult.InvalidData;

    if (packSize > (ulong)packedStreams.Length - packPos)
      return SevenZipArchiveReadResult.InvalidData;

    if (packPos > int.MaxValue || packSize > int.MaxValue || packPos + packSize > int.MaxValue)
      return SevenZipArchiveReadResult.NotSupported;

    if (unpackInfo.Folders.Length != 1 || unpackInfo.FolderUnpackSizes.Length != 1)
      return SevenZipArchiveReadResult.NotSupported;

    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[0];
    if (folderUnpackSizes is null || folderUnpackSizes.Length == 0)
      return SevenZipArchiveReadResult.InvalidData;

    SevenZipFolderDecodeResult folderDecodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        options: options,
        output: out decodedHeaderBytes);

    if (folderDecodeResult != SevenZipFolderDecodeResult.Ok)
    {
      decodedHeaderBytes = [];
      return folderDecodeResult == SevenZipFolderDecodeResult.NotSupported
          ? SevenZipArchiveReadResult.NotSupported
          : SevenZipArchiveReadResult.InvalidData;
    }

    switch (SevenZipHeaderReader.TryRead(decodedHeaderBytes, out decodedHeader, out int headerBytesConsumed))
    {
      case SevenZipHeaderReadResult.Ok:
        break;
      case SevenZipHeaderReadResult.NeedMoreInput:
        return SevenZipArchiveReadResult.InvalidData;
      case SevenZipHeaderReadResult.NotSupported:
        return SevenZipArchiveReadResult.NotSupported;
      default:
        return SevenZipArchiveReadResult.InvalidData;
    }

    if (headerBytesConsumed != decodedHeaderBytes.Length)
      return SevenZipArchiveReadResult.InvalidData;

    return SevenZipArchiveReadResult.Ok;
  }
}
