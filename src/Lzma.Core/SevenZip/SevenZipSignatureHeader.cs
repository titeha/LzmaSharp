using System.Buffers.Binary;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Сигнатурный заголовок 7z (первые 32 байта файла).
///
/// <para>
/// Формат (Little Endian):
/// [0..5]   - signature "7z\xBC\xAF\x27\x1C"
/// [6]      - versionMajor
/// [7]      - versionMinor
/// [8..11]  - StartHeaderCRC (CRC32 от 20 байт StartHeader)
/// [12..19] - NextHeaderOffset (UInt64 LE)
/// [20..27] - NextHeaderSize (UInt64 LE)
/// [28..31] - NextHeaderCRC (UInt32 LE)
/// </para>
///
/// <para>
/// На данном шаге мы валидируем StartHeaderCRC сразу при чтении.
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
  /// Размер сигнатурного заголовка 7z в байтах.
  /// </summary>
  public const int Size = 32;

  /// <summary>
  /// Размер сигнатуры.
  /// </summary>
  public const int SignatureSize = 6;

  private const int _startHeaderSize = 20;

  // 7z signature: 37 7A BC AF 27 1C
  private static ReadOnlySpan<byte> SignatureBytes => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

  /// <summary>
  /// Пытается прочитать сигнатурный заголовок 7z.
  /// </summary>
  public static SevenZipSignatureHeaderReadResult TryRead(
    ReadOnlySpan<byte> input,
    out SevenZipSignatureHeader header,
    out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    if (input.Length < SignatureSize)
      return SevenZipSignatureHeaderReadResult.NeedMoreInput;

    if (!input.Slice(0, SignatureSize).SequenceEqual(SignatureBytes))
      return SevenZipSignatureHeaderReadResult.InvalidData;

    if (input.Length < Size)
      return SevenZipSignatureHeaderReadResult.NeedMoreInput;

    byte versionMajor = input[6];
    byte versionMinor = input[7];

    uint startHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(8, 4));

    // StartHeaderCRC считается по 20 байт StartHeader (смещения 12..31).
    uint expectedStartHeaderCrc = Crc32.Compute(input.Slice(12, _startHeaderSize));
    if (expectedStartHeaderCrc != startHeaderCrc)
      return SevenZipSignatureHeaderReadResult.InvalidData;

    ulong nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(12, 8));
    ulong nextHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(20, 8));
    uint nextHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(28, 4));

    header = new SevenZipSignatureHeader(
      versionMajor,
      versionMinor,
      startHeaderCrc,
      nextHeaderOffset,
      nextHeaderSize,
      nextHeaderCrc);

    bytesConsumed = Size;
    return SevenZipSignatureHeaderReadResult.Ok;
  }
}

/// <summary>
/// Результат чтения сигнатурного заголовка 7z.
/// </summary>
public enum SevenZipSignatureHeaderReadResult
{
  Ok,
  NeedMoreInput,
  InvalidData,
}
