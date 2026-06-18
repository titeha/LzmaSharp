namespace Lzma.Core.SevenZip;

/// <summary>
/// Описание одного packed stream, используемого конкретным folder'ом.
/// Offset/Length — диапазон в буфере SevenZipArchiveReader.PackedStreams.
/// PackStreamIndex — глобальный индекс в PackInfo.PackSizes.
/// FolderInIndex — индекс входного потока folder'а (InIndex), который соответствует этому packed stream.
/// </summary>
public readonly struct SevenZipFolderPackedStreamRange(ulong folderInIndex, uint packStreamIndex, int offset, int length)
{
  public ulong FolderInIndex { get; } = folderInIndex;

  public uint PackStreamIndex { get; } = packStreamIndex;

  public int Offset { get; } = offset;

  public int Length { get; } = length;
}
