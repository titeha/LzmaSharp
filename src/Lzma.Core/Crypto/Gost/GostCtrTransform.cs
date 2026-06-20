using System.Security.Cryptography;

namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Общее ядро режима гаммирования (CTR) по ГОСТ Р 34.13-2015 для блочных шифров ГОСТ.
/// </summary>
/// <remarks>
/// Параметризуется блочным шифром и размером блока. Счётчик инициализируется как
/// IV || 0^(blockSize - ivSize) (IV — половина блока), затем шифруется и инкрементируется.
/// Шифрование и расшифрование в CTR совпадают.
/// </remarks>
internal static class GostCtrTransform
{
  /// <summary>Делегат блочного шифрования.</summary>
  public delegate bool EncryptBlock(ReadOnlySpan<byte> key, ReadOnlySpan<byte> block, Span<byte> output);

  public static bool Transform(
      EncryptBlock encrypt,
      int blockSize,
      int ivSize,
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> initializationVector,
      ReadOnlySpan<byte> input,
      Span<byte> output)
  {
    if (initializationVector.Length != ivSize)
      return false;

    if (output.Length < input.Length)
      return false;

    if (input.Length == 0)
      return true;

    Span<byte> counter = stackalloc byte[blockSize];
    Span<byte> gamma = stackalloc byte[blockSize];

    try
    {
      initializationVector.CopyTo(counter);
      counter[ivSize..].Clear();

      int offset = 0;
      while (offset < input.Length)
      {
        if (!encrypt(key, counter, gamma))
        {
          output[..input.Length].Clear();
          return false;
        }

        int chunkLength = Math.Min(blockSize, input.Length - offset);
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
