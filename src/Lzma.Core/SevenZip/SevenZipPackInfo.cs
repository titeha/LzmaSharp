namespace Lzma.Core.SevenZip;

public readonly struct SevenZipPackInfo(ulong packPos, ulong[] packSizes)
{
  public ulong PackPos { get; } = packPos;

  /// <summary>
  /// Размеры pack-stream'ов в байтах.
  /// </summary>
  public ulong[] PackSizes { get; } = packSizes ?? throw new ArgumentNullException(nameof(packSizes));
}
