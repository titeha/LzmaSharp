using System.Buffers.Binary;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

public enum SevenZipSignatureHeaderReadResult
{
  Done = 0,
  NeedMoreInput = 1,
  InvalidData = 2,
}

/// <summary>
/// Сигнатурный заголовок 7z (первые 32 байта файла).
/// </summary>
public readonly record struct SevenZipSignatureHeader(
    byte VersionMajor,
    byte VersionMinor,
    uint StartHeaderCrc,
    ulong NextHeaderOffset,
    ulong NextHeaderSize,
    uint NextHeaderCrc)
{
  public const int Size = 32;

  // В спецификации 7z фиксированы байты версии.
  public const byte MajorVersion = 0;
  public const byte MinorVersion = 4;

  private static ReadOnlySpan<byte> SignatureBytes => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

  public static ReadOnlySpan<byte> Signature => SignatureBytes;

  /// <summary>
  /// Удобный конструктор для тестов: по известным полям StartHeader
  /// вычисляет StartHeaderCrc.
  /// </summary>
  public SevenZipSignatureHeader(ulong nextHeaderOffset, ulong nextHeaderSize, uint nextHeaderCrc)
      : this(
          MajorVersion,
          MinorVersion,
          ComputeStartHeaderCrc(nextHeaderOffset, nextHeaderSize, nextHeaderCrc),
          nextHeaderOffset,
          nextHeaderSize,
          nextHeaderCrc)
  {
  }

  public static SevenZipSignatureHeaderReadResult TryRead(
      ReadOnlySpan<byte> input,
      out SevenZipSignatureHeader header,
      out int bytesConsumed)
  {
    header = default;
    bytesConsumed = 0;

    if (input.Length < Size)
      return SevenZipSignatureHeaderReadResult.NeedMoreInput;

    if (!SignatureBytes.SequenceEqual(input[..SignatureBytes.Length]))
      return SevenZipSignatureHeaderReadResult.InvalidData;

    var versionMajor = input[SignatureBytes.Length];
    var versionMinor = input[SignatureBytes.Length + 1];

    if (versionMajor != MajorVersion || versionMinor != MinorVersion)
      return SevenZipSignatureHeaderReadResult.InvalidData;

    var startHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(8, 4));
    var nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(12, 8));
    var nextHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(input.Slice(20, 8));
    var nextHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(28, 4));

    // StartHeaderCrc — CRC32 от 20 байт StartHeader (Offset+Size+NextHeaderCrc).
    var expectedStartHeaderCrc = Crc32.Compute(input.Slice(12, 20));
    if (expectedStartHeaderCrc != startHeaderCrc)
      return SevenZipSignatureHeaderReadResult.InvalidData;

    header = new SevenZipSignatureHeader(
        versionMajor,
        versionMinor,
        startHeaderCrc,
        nextHeaderOffset,
        nextHeaderSize,
        nextHeaderCrc);

    bytesConsumed = Size;
    return SevenZipSignatureHeaderReadResult.Done;
  }

  private static uint ComputeStartHeaderCrc(ulong nextHeaderOffset, ulong nextHeaderSize, uint nextHeaderCrc)
  {
    Span<byte> tmp = stackalloc byte[20];
    BinaryPrimitives.WriteUInt64LittleEndian(tmp[..8], nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(tmp.Slice(8, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(tmp.Slice(16, 4), nextHeaderCrc);
    return Crc32.Compute(tmp);
  }
}
