using System.Security.Cryptography;

using Lzma.Core.Crypto.Gost;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат шифрования упакованного потока через экспериментальный ГОСТ-кодер.
/// </summary>
public enum SevenZipGostEncryptResult
{
  /// <summary>
  /// Шифрование выполнено успешно.
  /// </summary>
  Ok = 0,

  /// <summary>
  /// Свойства кодера или идентификатор метода некорректны.
  /// </summary>
  InvalidData = 1,

  /// <summary>
  /// Сценарий корректно распознан, но пока не поддерживается.
  /// </summary>
  NotSupported = 2,
}

/// <summary>
/// Шифрование упакованных потоков для экспериментальных ГОСТ-кодеров (сторона записи).
/// </summary>
/// <remarks>
/// Зеркально <see cref="SevenZipGostPackedStreamDecryptor"/>. Режим CTR симметричен,
/// поэтому шифрование сводится к формированию ключа и тому же CTR-преобразованию.
/// Поддержаны Кузнечик и Магма; формирование ключа — direct-key
/// (<see cref="SevenZipGostCoder.DirectKeyNumCyclesPower"/>) и парольный KDF через
/// Стрибог-256 в пределах <see cref="SevenZipGostCoder.SupportedNumCyclesPowerMax"/>.
/// </remarks>
public static class SevenZipGostPackedStreamEncryptor
{
  /// <summary>
  /// Пытается зашифровать упакованный поток через парольный материал.
  /// </summary>
  /// <param name="methodId">Идентификатор метода ГОСТ-кодера.</param>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="password">Парольный материал архива.</param>
  /// <param name="plaintext">Исходный упакованный поток.</param>
  /// <param name="ciphertext">Зашифрованный поток при успешном результате.</param>
  /// <returns>Результат попытки шифрования.</returns>
  public static SevenZipGostEncryptResult TryEncrypt(
      ReadOnlySpan<byte> methodId,
      SevenZipGostProperties properties,
      SevenZipPassword password,
      ReadOnlySpan<byte> plaintext,
      out byte[] ciphertext)
  {
    ArgumentNullException.ThrowIfNull(properties);
    ArgumentNullException.ThrowIfNull(password);

    ciphertext = [];

    if (!SevenZipGostCoder.IsGostMethodId(methodId))
      return SevenZipGostEncryptResult.InvalidData;

    if (!SevenZipGostCoder.IsSupportedNumCyclesPower(properties.NumCyclesPower))
      return SevenZipGostEncryptResult.NotSupported;

    bool directKey = properties.NumCyclesPower == SevenZipGostCoder.DirectKeyNumCyclesPower;

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      bool derived = directKey
          ? SevenZipGostKeyDerivation.TryDeriveDirectKey(properties, password, key)
          : SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key);

      if (!derived)
        return SevenZipGostEncryptResult.InvalidData;

      return TryEncrypt(
          methodId: methodId,
          properties: properties,
          key: key,
          plaintext: plaintext,
          ciphertext: out ciphertext);
    }
    finally
    {
      CryptographicOperations.ZeroMemory(key);
    }
  }

  /// <summary>
  /// Пытается зашифровать упакованный поток готовым ключом.
  /// </summary>
  /// <param name="methodId">Идентификатор метода ГОСТ-кодера.</param>
  /// <param name="properties">Разобранные свойства ГОСТ-кодера.</param>
  /// <param name="key">Готовый 256-битный ключ.</param>
  /// <param name="plaintext">Исходный упакованный поток.</param>
  /// <param name="ciphertext">Зашифрованный поток при успешном результате.</param>
  /// <returns>Результат попытки шифрования.</returns>
  public static SevenZipGostEncryptResult TryEncrypt(
      ReadOnlySpan<byte> methodId,
      SevenZipGostProperties properties,
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> plaintext,
      out byte[] ciphertext)
  {
    ArgumentNullException.ThrowIfNull(properties);

    ciphertext = [];

    if (SevenZipGostCoder.IsKuznyechikMethodId(methodId))
    {
      if (!SevenZipGostInitializationVector.TryBuildKuznyechikCtr(
          properties,
          out byte[] initializationVector))
        return SevenZipGostEncryptResult.InvalidData;

      try
      {
        if (!GostKuznyechikCtrTransform.TryTransform(
            key,
            initializationVector,
            plaintext,
            out ciphertext))
        {
          ciphertext = [];
          return SevenZipGostEncryptResult.InvalidData;
        }

        return SevenZipGostEncryptResult.Ok;
      }
      finally
      {
        Array.Clear(initializationVector);
      }
    }

    if (SevenZipGostCoder.IsMagmaMethodId(methodId))
    {
      if (!SevenZipGostInitializationVector.TryBuildMagmaCtr(
          properties,
          out byte[] initializationVector))
        return SevenZipGostEncryptResult.InvalidData;

      try
      {
        if (!GostMagmaCtrTransform.TryTransform(
            key,
            initializationVector,
            plaintext,
            out ciphertext))
        {
          ciphertext = [];
          return SevenZipGostEncryptResult.InvalidData;
        }

        return SevenZipGostEncryptResult.Ok;
      }
      finally
      {
        Array.Clear(initializationVector);
      }
    }

    return SevenZipGostEncryptResult.InvalidData;
  }
}
