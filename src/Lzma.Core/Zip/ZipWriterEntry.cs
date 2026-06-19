namespace Lzma.Core.Zip;

/// <summary>
/// Описывает элемент, который writer должен положить в ZIP-архив.
/// Имя использует <c>/</c> как разделитель; для директорий имя оканчивается на <c>/</c>.
/// </summary>
public sealed record ZipWriterEntry(string Name, byte[] Content, bool IsDirectory = false);
