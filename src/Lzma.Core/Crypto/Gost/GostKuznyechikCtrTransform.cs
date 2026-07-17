namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Режим гаммирования (CTR) для Кузнечика. Использует общее ядро <see cref="GostCtrTransform"/>.
/// </summary>
public static class GostKuznyechikCtrTransform
{
  /// <summary>
  /// Размер IV в байтах для режима CTR по ГОСТ Р 34.13-2015.
  /// </summary>
  public const int InitializationVectorSize = 8;

  /// <summary>
  /// Пытается выполнить CTR-преобразование.
  /// </summary>
  /// <remarks>
  /// Для CTR шифрование и расшифрование совпадают.
  /// </remarks>
  public static bool TryTransform(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> input,
      Span<byte> output)
  {
    if (key.Length != GostKuznyechikCipher.KeySize)
      return false;

    // Расписание ключа Кузнечика — дорогое (L-преобразования). Считаем его ОДИН раз на всё
    // преобразование и шифруем им все блоки-счётчики (раньше пересчёт на каждый блок давал ~0.04 МиБ/с).
    byte[][] roundKeys = GostKuznyechikCipher.ExpandRoundKeys(key);
    try
    {
      return GostCtrTransform.Transform(
          (counterBlock, gamma) => GostKuznyechikCipher.EncryptBlock(roundKeys, counterBlock, gamma),
          GostKuznyechikCipher.BlockSize,
          InitializationVectorSize,
          initializationVector,
          input,
          output);
    }
    finally
    {
      GostKuznyechikCipher.ZeroRoundKeys(roundKeys);
    }
  }

  /// <summary>
  /// Пытается выполнить CTR-преобразование.
  /// </summary>
  public static bool TryTransform(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> input,
      out byte[] output)
  {
    output = new byte[input.Length];

    if (!TryTransform(
        key,
        initializationVector,
        input,
        output))
    {
      output = [];
      return false;
    }

    return true;
  }
}
