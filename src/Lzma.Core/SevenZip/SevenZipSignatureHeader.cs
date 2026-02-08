using System.Buffers.Binary;

using Lzma.Core.Checksums;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Заголовок сигнатуры 7z (Signature Header).
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

  // Историческое имя, которое используется в некоторых тестах.
  public const int TotalSize = Size;

  // Официальная сигнатура 7z: 37 7A BC AF 27 1C
  private static readonly byte[] _signature =
  [
    0x37,
    0x7A,
    0xBC,
    0xAF,
    0x27,
    0x1C,
  ];

  private const int _signatureSize = 6;

  // Эти значения используются в тестах и соответствуют распространённому варианту формата 7z.
  // (Major = 0, Minor = 4)
  public const byte MajorVersion = 0;
  public const byte MinorVersion = 4;

  /// <summary>Сигнатура 7z (6 байт).</summary>
  public static ReadOnlySpan<byte> Signature => _signature;

  private const int _startHeaderSize = 20;

  /// <summary>
  /// Удобный конструктор для тестов: версия берётся из констант,
  /// CRC стартового заголовка вычисляется автоматически.
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

  public enum ReadResult
  {
    Ok = 0,
    NeedMoreInput = 1,
    InvalidData = 2,
  }

  /// <summary>
  /// Записать заголовок в буфер (ровно 32 байта).
  /// </summary>
  public void Write(Span<byte> output)
  {
    if (output.Length < Size)
      throw new ArgumentOutOfRangeException(nameof(output), $"Буфер слишком маленький (нужно минимум {Size} байт)");

    Signature.CopyTo(output);

    output[6] = VersionMajor;
    output[7] = VersionMinor;

    // StartHeader = [NextHeaderOffset (8), NextHeaderSize (8), NextHeaderCrc (4)]
    // CRC считается именно по этим 20 байтам.
    BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(12, 8), NextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(output.Slice(20, 8), NextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(28, 4), NextHeaderCrc);

    // Для удобства тестов: если CRC не задан (0), вычисляем его автоматически.
    // Это позволяет создавать корректный заголовок, не дублируя логику расчёта CRC снаружи.
    uint startHeaderCrc = StartHeaderCrc != 0
      ? StartHeaderCrc
      : Crc32.Compute(output.Slice(12, _startHeaderSize));

    BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(8, 4), startHeaderCrc);
  }

  /// <summary>
  /// Удобная перегрузка для byte[].
  /// </summary>
  public void Write(byte[] output) => Write(output.AsSpan());

  /// <summary>
  /// Перегрузка для «потокового» чтения: возвращает ещё и количество потреблённых байт.
  /// </summary>
  public static ReadResult TryRead(ReadOnlySpan<byte> input, out SevenZipSignatureHeader header, out int bytesConsumed)
  {
    ReadResult result = TryRead(input, out header);

    // Важно для инкрементального парсинга: если данных недостаточно или они некорректные,
    // мы НЕ «съедаем» байты. Заголовок сигнатуры должен читаться атомарно.
    bytesConsumed = result == ReadResult.Ok ? Size : 0;

    return result;
  }

  public static ReadResult TryRead(ReadOnlySpan<byte> input, out SevenZipSignatureHeader header)
  {
    header = default;

    if (input.Length < Size)
      return ReadResult.NeedMoreInput;

    if (!input[.._signatureSize].SequenceEqual(Signature))
      return ReadResult.InvalidData;

    byte versionMajor = input[6];
    byte versionMinor = input[7];

    uint startHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(8, 4));

    // StartHeader = [NextHeaderOffset (8), NextHeaderSize (8), NextHeaderCrc (4)]
    var startHeader = input.Slice(12, _startHeaderSize);
    if (!Crc32.TryReadAndCheckCrc(startHeader, startHeaderCrc, out _))
      return ReadResult.InvalidData;

    ulong nextHeaderOffset = BinaryPrimitives.ReadUInt64LittleEndian(startHeader[..8]);
    ulong nextHeaderSize = BinaryPrimitives.ReadUInt64LittleEndian(startHeader.Slice(8, 8));
    uint nextHeaderCrc = BinaryPrimitives.ReadUInt32LittleEndian(startHeader.Slice(16, 4));

    header = new SevenZipSignatureHeader(
      versionMajor,
      versionMinor,
      startHeaderCrc,
      nextHeaderOffset,
      nextHeaderSize,
      nextHeaderCrc);

    return ReadResult.Ok;
  }

  private static uint ComputeStartHeaderCrc(ulong nextHeaderOffset, ulong nextHeaderSize, uint nextHeaderCrc)
  {
    Span<byte> startHeader = stackalloc byte[_startHeaderSize];
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader[..8], nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(8, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(startHeader.Slice(16, 4), nextHeaderCrc);

    return Crc32.Compute(startHeader);
  }
}
