using System.Buffers.Binary;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат попытки прочитать 7z SignatureHeader (первые 32 байта файла .7z).
/// </summary>
public enum SevenZipSignatureHeaderReadResult
{
  /// <summary>
  /// Заголовок успешно прочитан.
  /// </summary>
  Ok,

  /// <summary>
  /// Входного буфера не хватило (нужно больше байт).
  /// </summary>
  NeedMoreInput,

  /// <summary>
  /// Данные некорректны (не похожи на 7z).
  /// </summary>
  InvalidData,
}

/// <summary>
/// <para>7z SignatureHeader.</para>
/// <para>
/// Формат (всего 32 байта):
/// - Signature (6 байт): 37 7A BC AF 27 1C
/// - Version (2 байта): major/minor
/// - StartHeaderCRC (4 байта, LE)
/// - NextHeaderOffset (8 байт, LE)
/// - NextHeaderSize (8 байт, LE)
/// - NextHeaderCRC (4 байта, LE)
/// </para>
/// <para>
/// На этом шаге мы только читаем структуру заголовка. Проверку CRC добавим отдельным шагом.
/// </para>
/// </summary>
public readonly record struct SevenZipSignatureHeader(
  byte VersionMajor,
  byte VersionMinor,
  uint StartHeaderCrc,
  ulong NextHeaderOffset,
  ulong NextHeaderSize,
  uint NextHeaderCrc)
{
  /// <summary>
  /// Длина SignatureHeader в байтах.
  /// </summary>
  public const int Size = 32;

  /// <summary>
  /// Абсолютная позиция NextHeader в файле (в байтах от начала файла).
  /// </summary>
  public ulong NextHeaderAbsoluteOffset => Size + NextHeaderOffset;

  /// <summary>
  /// Пробует прочитать SignatureHeader.
  /// </summary>
  /// <param name="input">Входные данные.</param>
  /// <param name="header">Распарсенный заголовок (если Ok).</param>
  /// <param name="bytesConsumed">Сколько байт было прочитано из input.</param>
  public static SevenZipSignatureHeaderReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipSignatureHeader header,
    out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    // Минимум 6 байт нужно хотя бы чтобы проверить сигнатуру.
    if (input.Length < 6)
      return SevenZipSignatureHeaderReadResult.NeedMoreInput;

    // Signature: 37 7A BC AF 27 1C
    if (input[0] != 0x37 ||
        input[1] != 0x7A ||
        input[2] != 0xBC ||
        input[3] != 0xAF ||
        input[4] != 0x27 ||
        input[5] != 0x1C)
    {
      return SevenZipSignatureHeaderReadResult.InvalidData;
    }

    // Теперь нам нужно целиком 32 байта.
    if (input.Length < Size)
      return SevenZipSignatureHeaderReadResult.NeedMoreInput;

    byte major = input[6];
    byte minor = input[7];

    uint startHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(8, 4));
    ulong nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(12, 8));
    ulong nextHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(20, 8));
    uint nextHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(28, 4));

    header = new SevenZipSignatureHeader(
      major,
      minor,
      startHeaderCrc,
      nextHeaderOffset,
      nextHeaderSize,
      nextHeaderCrc);

    bytesConsumed = Size;
    return SevenZipSignatureHeaderReadResult.Ok;
  }
}
