namespace Lzma.Core.Zip;

/// <summary>
/// Метаданные одного элемента ZIP-архива, прочитанные из центрального каталога БЕЗ распаковки
/// данных. Размеры/смещение — <see cref="long"/> (готово к ZIP64). Данные извлекаются потоково по
/// <see cref="LocalHeaderOffset"/> отдельно.
/// </summary>
/// <remarks>
/// <see cref="Method"/> — ЭФФЕКТИВНЫЙ метод сжатия (Store/Deflate). Для WinZip-AES это реальный метод
/// из extra-поля 0x9901, а <see cref="IsEncrypted"/> взведён и <see cref="AesStrength"/> задаёт силу
/// (для извлечения нужен пароль).
/// </remarks>
public readonly record struct ZipStreamEntry(
    string Name,
    ushort Method,
    uint Crc,
    long CompressedSize,
    long UncompressedSize,
    long LocalHeaderOffset,
    bool IsDirectory,
    ushort Flags,
    bool IsEncrypted = false,
    WinZipAes.Strength AesStrength = default);
