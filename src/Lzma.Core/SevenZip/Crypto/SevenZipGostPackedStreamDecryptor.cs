using System.Security.Cryptography;

using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат расшифровки упакованного потока через экспериментальный ГОСТ-кодер.
/// </summary>
public enum SevenZipGostDecryptResult
{
  /// <summary>
  /// Расшифровка выполнена успешно.
  /// </summary>
  Ok = 0,

  /// <summary>
  /// Данные, свойства кодера или идентификатор метода некорректны.
  /// </summary>
  InvalidData = 1,

  /// <summary>
  /// Сценарий корректно распознан, но пока не поддерживается.
  /// </summary>
  NotSupported = 2,
}

/// <summary>
/// Расшифровка упакованных потоков для экспериментальных ГОСТ-кодеров.
/// </summary>
/// <remarks>
/// Сейчас поддержан только Кузнечик в режиме CTR и только тестовый direct-key
/// режим формирования ключа. Магма и полноценный ГОСТ-KDF пока возвращают
/// <see cref="SevenZipGostDecryptResult.NotSupported"/>.
/// </remarks>
public static class SevenZipGostPackedStreamDecryptor
{
  /// <summary>
  /// Пытается расшифровать упакованный поток через парольный материал.
  /// </summary>
  /// <remarks>
  /// Этот overload сам формирует ключ из свойств ГОСТ-кодера и пароля.
  /// Пока поддержан только direct-key режим <see cref="SevenZipGostCoder.DirectKeyNumCyclesPower"/>.
  /// </remarks>
  /// <param name="methodId">Идентификатор метода ГОСТ-кодера.</param>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="password">Парольный материал архива.</param>
  /// <param name="ciphertext">Зашифрованный упакованный поток.</param>
  /// <param name="plaintext">Расшифрованный поток при успешном результате.</param>
  /// <returns>Результат попытки расшифровки.</returns>
  public static SevenZipGostDecryptResult TryDecrypt(
      ReadOnlySpan<byte> methodId,
      SevenZipGostProperties properties,
      SevenZipPassword password,
      ReadOnlySpan<byte> ciphertext,
      out byte[] plaintext)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    plaintext = [];

    if (SevenZipGostCoder.IsMagmaMethodId(methodId))
    {
      plaintext = [];
      return SevenZipGostDecryptResult.NotSupported;
    }

    if (!SevenZipGostCoder.IsKuznyechikMethodId(methodId))
    {
      plaintext = [];
      return SevenZipGostDecryptResult.InvalidData;
    }

    if (properties.NumCyclesPower != SevenZipGostCoder.DirectKeyNumCyclesPower)
    {
      plaintext = [];
      return SevenZipGostDecryptResult.NotSupported;
    }

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      if (!SevenZipGostKeyDerivation.TryDeriveDirectKey(
          properties,
          password,
          key))
      {
        plaintext = [];
        return SevenZipGostDecryptResult.InvalidData;
      }

      return TryDecrypt(
          methodId: methodId,
          properties: properties,
          key: key,
          ciphertext: ciphertext,
          plaintext: out plaintext);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  /// <summary>
  /// Пытается расшифровать упакованный поток готовым ключом.
  /// </summary>
  /// <remarks>
  /// Этот overload не выполняет формирование ключа и используется после того,
  /// как ключ уже получен вызывающим кодом.
  /// </remarks>
  /// <param name="methodId">Идентификатор метода ГОСТ-кодера.</param>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="key">Готовый 256-битный ключ.</param>
  /// <param name="ciphertext">Зашифрованный упакованный поток.</param>
  /// <param name="plaintext">Расшифрованный поток при успешном результате.</param>
  /// <returns>Результат попытки расшифровки.</returns>
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
