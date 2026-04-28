namespace Lzma.Core.SevenZip;

/// <summary>
/// Подготовка initialization vector для экспериментальных GOST coder-ов.
/// </summary>
public static class SevenZipGostInitializationVector
{
  /// <summary>
  /// Размер IV в байтах для текущего сценария Кузнечик + CTR.
  /// </summary>
  public const int KuznyechikCtrInitializationVectorSize = 8;

  /// <summary>
  /// Пытается построить IV для текущего сценария Кузнечик + CTR.
  /// </summary>
  public static bool TryBuildKuznyechikCtr(
      SevenZipGostProperties properties,
      Span<byte> destination)
  {
    ArgumentNullException.ThrowIfNull(properties);

    if (destination.Length < KuznyechikCtrInitializationVectorSize)
      throw new ArgumentException("Буфер назначения меньше размера IV для Кузнечика в CTR.", nameof(destination));

    if (properties.InitializationVector.Length != KuznyechikCtrInitializationVectorSize)
      return false;

    properties.InitializationVector.CopyTo(destination[..KuznyechikCtrInitializationVectorSize]);
    return true;
  }

  /// <summary>
  /// Пытается построить IV для текущего сценария Кузнечик + CTR.
  /// </summary>
  public static bool TryBuildKuznyechikCtr(
      SevenZipGostProperties properties,
      out byte[] initializationVector)
  {
    initializationVector = new byte[KuznyechikCtrInitializationVectorSize];

    if (!TryBuildKuznyechikCtr(
        properties,
        initializationVector))
    {
      initializationVector = [];
      return false;
    }

    return true;
  }
}
