namespace Lzma.Core.SevenZip;

/// <summary>
/// Описывает элемент, который writer должен положить в 7z-архив.
/// </summary>
public sealed record SevenZipArchiveWriterEntry(
    string Name,
    byte[] Content,
    bool IsDirectory = false);
