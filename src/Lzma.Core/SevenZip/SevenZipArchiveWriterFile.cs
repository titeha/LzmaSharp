namespace Lzma.Core.SevenZip;

/// <summary>
/// Описывает файл, который writer должен положить в 7z-архив.
/// </summary>
public sealed record SevenZipArchiveWriterFile(
    string Name,
    byte[] Content);
