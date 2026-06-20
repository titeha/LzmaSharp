namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Режим гаммирования (CTR) для Магмы. Использует общее ядро <see cref="GostCtrTransform"/>.
/// </summary>
public static class GostMagmaCtrTransform
{
  /// <summary>
  /// Размер IV в байтах для режима CTR по ГОСТ Р 34.13-2015 (половина 64-битного блока).
  /// </summary>
  public const int InitializationVectorSize = 4;

  /// <summary>Пытается выполнить CTR-преобразование (шифрование = расшифрование).</summary>
  public static bool TryTransform(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> input,
      Span<byte> output)
  {
    if (key.Length != GostMagmaCipher.KeySize)
      return false;

    return GostCtrTransform.Transform(
        GostMagmaCipher.TryEncryptBlock,
        GostMagmaCipher.BlockSize,
        InitializationVectorSize,
        key,
        initializationVector,
        input,
        output);
  }

  /// <summary>Пытается выполнить CTR-преобразование.</summary>
  public static bool TryTransform(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> input,
      out byte[] output)
  {
    output = new byte[input.Length];

    if (!TryTransform(key, initializationVector, input, output))
    {
      output = [];
      return false;
    }

    return true;
  }
}
