namespace Lzma.Core.SevenZip;

/// <summary>
/// Подготовка вектора инициализации для экспериментальных ГОСТ-кодеров.
/// </summary>
/// <remarks>
/// Сейчас используется только сценарий Кузнечик в режиме CTR.
/// </remarks>
public static class SevenZipGostInitializationVector
{
  /// <summary>
  /// Размер вектора инициализации в байтах для текущего сценария Кузнечик + CTR.
  /// </summary>
  public const int KuznyechikCtrInitializationVectorSize = 8;

  /// <summary>
  /// Пытается построить вектор инициализации для текущего сценария Кузнечик + CTR.
  /// </summary>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="destination">Буфер, куда будет записан вектор инициализации.</param>
  /// <returns>
  /// <see langword="true"/>, если вектор инициализации удалось построить;
  /// иначе <see langword="false"/>.
  /// </returns>
  public static bool TryBuildKuznyechikCtr(
      SevenZipGostProperties properties,
      Span<byte> destination)
  {
    ArgumentNullException.ThrowIfNull(properties);

    if (destination.Length < KuznyechikCtrInitializationVectorSize)
      throw new ArgumentException("Буфер назначения меньше размера вектора инициализации для Кузнечика в CTR.", nameof(destination));

    if (properties.InitializationVector.Length != KuznyechikCtrInitializationVectorSize)
      return false;

    properties.InitializationVector.CopyTo(destination[..KuznyechikCtrInitializationVectorSize]);
    return true;
  }

  /// <summary>
  /// Пытается построить вектор инициализации для текущего сценария Кузнечик + CTR.
  /// </summary>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="initializationVector">
  /// Вектор инициализации при успешном результате; иначе пустой массив.
  /// </param>
  /// <returns>
  /// <see langword="true"/>, если вектор инициализации удалось построить;
  /// иначе <see langword="false"/>.
  /// </returns>
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

  /// <summary>
  /// Размер вектора инициализации в байтах для сценария Магма + CTR (половина 64-битного блока).
  /// </summary>
  public const int MagmaCtrInitializationVectorSize = 4;

  /// <summary>
  /// Пытается построить вектор инициализации для сценария Магма + CTR.
  /// </summary>
  public static bool TryBuildMagmaCtr(
      SevenZipGostProperties properties,
      out byte[] initializationVector)
  {
    ArgumentNullException.ThrowIfNull(properties);

    initializationVector = new byte[MagmaCtrInitializationVectorSize];

    if (properties.InitializationVector.Length != MagmaCtrInitializationVectorSize)
    {
      initializationVector = [];
      return false;
    }

    properties.InitializationVector.CopyTo(initializationVector);
    return true;
  }
}
