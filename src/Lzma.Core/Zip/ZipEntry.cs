namespace Lzma.Core.Zip;

/// <summary>
/// Распакованный элемент ZIP-архива (файл или директория).
/// Для директорий <see cref="Bytes"/> пустой.
/// </summary>
public readonly record struct ZipEntry(string Name, byte[] Bytes, bool IsDirectory);
