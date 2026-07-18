namespace Lzma.Core.Zip;

/// <summary>
/// Метаданные одного элемента ZIP-архива, прочитанные из центрального каталога БЕЗ распаковки
/// данных. Размеры/смещение — <see cref="long"/> (готово к ZIP64). Данные извлекаются потоково по
/// <see cref="LocalHeaderOffset"/> отдельно.
/// </summary>
public readonly record struct ZipStreamEntry(
    string Name,
    ushort Method,
    uint Crc,
    long CompressedSize,
    long UncompressedSize,
    long LocalHeaderOffset,
    bool IsDirectory,
    ushort Flags);
