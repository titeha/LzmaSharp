namespace Lzma.Core.SevenZip;

/// <summary>
/// StreamsInfo из спецификации 7z.
/// Содержит описания Pack/Unpack/SubStreams.
/// </summary>
public sealed class SevenZipStreamsInfo(
  SevenZipPackInfo? packInfo,
  SevenZipUnpackInfo? unpackInfo,
  SevenZipSubStreamsInfo? subStreamsInfo)
{
  public SevenZipPackInfo? PackInfo { get; } = packInfo;
  public SevenZipUnpackInfo? UnpackInfo { get; } = unpackInfo;
  public SevenZipSubStreamsInfo? SubStreamsInfo { get; } = subStreamsInfo;
}
