namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат декодирования элемента 7z (файл или директория).
/// Для директорий Bytes всегда пустой.
/// </summary>
public readonly record struct SevenZipDecodedEntry(string Name, byte[] Bytes, bool IsDirectory);
