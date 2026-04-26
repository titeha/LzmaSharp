using System.Security.Cryptography;

namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Режим гаммирования (CTR) для Кузнечика.
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

    if (initializationVector.Length != InitializationVectorSize)
      return false;

    if (output.Length < input.Length)
      return false;

    if (input.Length == 0)
      return true;

    Span<byte> counter = stackalloc byte[GostKuznyechikCipher.BlockSize];
    Span<byte> gamma = stackalloc byte[GostKuznyechikCipher.BlockSize];

    try
    {
      initializationVector.CopyTo(counter);
      counter[InitializationVectorSize..].Clear();

      int offset = 0;

      while (offset < input.Length)
      {
        if (!GostKuznyechikCipher.TryEncryptBlock(
            key,
            counter,
            gamma))
        {
          output[..input.Length].Clear();
          return false;
        }

        int chunkLength = Math.Min(
            GostKuznyechikCipher.BlockSize,
            input.Length - offset);

        for (int i = 0; i < chunkLength; i++)
          output[offset + i] = (byte)(input[offset + i] ^ gamma[i]);

        IncrementCounter(counter);
        offset += chunkLength;
      }

      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(counter);
      CryptographicOperations.ZeroMemory(gamma);
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

  private static void IncrementCounter(Span<byte> counter)
  {
    for (int i = counter.Length - 1; i >= 0; i--)
    {
      counter[i]++;

      if (counter[i] != 0)
        break;
    }
  }
}
