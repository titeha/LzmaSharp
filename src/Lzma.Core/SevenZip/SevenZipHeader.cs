namespace Lzma.Core.SevenZip;

/// <summary>
/// Распарсенный NextHeader типа <see cref="SevenZipNextHeaderKind.Header"/>.
/// На текущем шаге мы храним только две основные секции:
/// - MainStreamsInfo (потоки/папки/сабстримы)
/// - FilesInfo (количество файлов и их свойства)
/// </summary>
public readonly struct SevenZipHeader(SevenZipStreamsInfo streamsInfo, SevenZipFilesInfo filesInfo)
{
  public SevenZipStreamsInfo StreamsInfo { get; } = streamsInfo;

  public SevenZipFilesInfo FilesInfo { get; } = filesInfo;
}
