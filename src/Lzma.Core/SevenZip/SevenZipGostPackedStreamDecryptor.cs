using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат расшифровки packed stream через экспериментальный GOST coder.
/// </summary>
public enum SevenZipGostDecryptResult
{
  /// <summary>
  /// Расшифровка выполнена успешно.
  /// </summary>
  Ok = 0,

  /// <summary>
  /// Данные или свойства coder-а некорректны.
  /// </summary>
  InvalidData = 1,

  /// <summary>
  /// Сценарий корректно распознан, но пока не поддерживается.
  /// </summary>
  NotSupported = 2,
}

/// <summary>
/// Расшифровка packed stream для экспериментальных GOST coder-ов.
/// </summary>
public static class SevenZipGostPackedStreamDecryptor
{
  /// <summary>
  /// Пытается расшифровать packed stream.
  /// </summary>
  public static SevenZipGostDecryptResult TryDecrypt(
      ReadOnlySpan<byte> methodId,
      SevenZipGostProperties properties,
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> ciphertext,
      out byte[] plaintext)
  {
    ArgumentNullException.ThrowIfNull(properties);

    plaintext = [];

    if (SevenZipGostCoder.IsKuznyechikMethodId(methodId))
    {
      if (!SevenZipGostInitializationVector.TryBuildKuznyechikCtr(
          properties,
          out byte[] initializationVector))
      {
        plaintext = [];
        return SevenZipGostDecryptResult.InvalidData;
      }

      try
      {
        if (!GostKuznyechikCtrTransform.TryTransform(
            key,
            initializationVector,
            ciphertext,
            out plaintext))
        {
          plaintext = [];
          return SevenZipGostDecryptResult.InvalidData;
        }

        return SevenZipGostDecryptResult.Ok;
      }
      finally
      {
        Array.Clear(initializationVector);
      }
    }

    if (SevenZipGostCoder.IsMagmaMethodId(methodId))
    {
      plaintext = [];
      return SevenZipGostDecryptResult.NotSupported;
    }

    plaintext = [];
    return SevenZipGostDecryptResult.InvalidData;
  }
}
