namespace Lzma.Core.SevenZip;

/// <summary>
/// Распарсенный NextHeader типа <see cref="SevenZipNextHeaderKind.Header"/>.
/// На текущем шаге мы храним только две основные секции:
/// - MainStreamsInfo (потоки/папки/сабстримы)
/// - FilesInfo (количество файлов и их свойства)
/// </summary>
public readonly struct SevenZipHeader
{
  public SevenZipHeader(SevenZipStreamsInfo streamsInfo, SevenZipFilesInfo filesInfo, byte[]? archiveProperties = null)
  {
    StreamsInfo = streamsInfo;
    FilesInfo = filesInfo;
    ArchiveProperties = archiveProperties;
  }

  public SevenZipStreamsInfo StreamsInfo { get; }

  public SevenZipFilesInfo FilesInfo { get; }

  /// <summary>
  /// Опциональные свойства архива (NID::kArchiveProperties).
  /// На текущем этапе мы их не парсим, поэтому здесь может быть null.
  /// </summary>
  public byte[]? ArchiveProperties { get; }
}
