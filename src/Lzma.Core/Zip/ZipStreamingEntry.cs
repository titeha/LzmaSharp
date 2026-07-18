namespace Lzma.Core.Zip;

/// <summary>
/// Элемент для ПОТОКОВОЙ записи ZIP: имя, размер и ленивое открытие потока данных. Данные читаются по
/// требованию (не держим весь набор файлов в памяти); для директорий <see cref="OpenRead"/> не вызывается.
/// </summary>
public sealed record ZipStreamingEntry(string Name, long Length, Func<Stream> OpenRead, bool IsDirectory = false);
