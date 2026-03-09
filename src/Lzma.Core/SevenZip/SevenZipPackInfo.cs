namespace Lzma.Core.SevenZip;

/// <summary>
/// PackInfo из заголовка 7z.
/// Содержит PackPos, размеры packed stream'ов, а также (опционально) CRC32 packed stream'ов (PackInfo.kCRC).
/// </summary>
public readonly struct SevenZipPackInfo
{
  public ulong PackPos { get; }

  public ulong[] PackSizes { get; }

  /// <summary>
  /// PackInfo.kCRC: флаг "CRC определён" для каждого packed stream.
  /// Длина = PackSizes.Length. Если секция kCRC отсутствует — null.
  /// </summary>
  public bool[]? CrcDefined { get; }

  /// <summary>
  /// PackInfo.kCRC: CRC32 для каждого packed stream.
  /// Длина = PackSizes.Length. Если CRC не определён (CrcDefined=false), значение может быть 0.
  /// </summary>
  public uint[]? Crc { get; }

  public bool HasCrc => CrcDefined is not null;

  public SevenZipPackInfo(ulong packPos, ulong[] packSizes, bool[]? crcDefined = null, uint[]? crc = null)
  {
    PackPos = packPos;
    PackSizes = packSizes ?? throw new ArgumentNullException(nameof(packSizes));

    if (crcDefined is null != crc is null)
      throw new ArgumentException("crcDefined и crc должны быть оба null, либо оба не null.");

    if (crcDefined is not null)
    {
      if (crcDefined.Length != PackSizes.Length)
        throw new ArgumentException("Длина crcDefined должна совпадать с PackSizes.Length.", nameof(crcDefined));

      if (crc!.Length != PackSizes.Length)
        throw new ArgumentException("Длина crc должна совпадать с PackSizes.Length.", nameof(crc));
    }

    CrcDefined = crcDefined;
    Crc = crc;
  }
}
