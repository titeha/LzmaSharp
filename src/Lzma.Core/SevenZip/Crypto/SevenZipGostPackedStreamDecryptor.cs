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
/// Поддержаны Кузнечик и Магма в режиме CTR. Формирование ключа: тестовый
/// direct-key режим (<see cref="SevenZipGostCoder.DirectKeyNumCyclesPower"/>)
/// и парольный KDF через Стрибог-256 для numCyclesPower в пределах
/// <see cref="SevenZipGostCoder.SupportedNumCyclesPowerMax"/>.
/// </remarks>
public static class SevenZipGostPackedStreamDecryptor
{
  /// <summary>
  /// Пытается расшифровать упакованный поток через парольный материал.
  /// </summary>
  /// <remarks>
  /// Этот overload сам формирует ключ из свойств ГОСТ-кодера и пароля:
  /// direct-key режим <see cref="SevenZipGostCoder.DirectKeyNumCyclesPower"/>
  /// либо парольный KDF через Стрибог-256 для остальных numCyclesPower.
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

    if (!SevenZipGostCoder.IsGostMethodId(methodId))
    {
      plaintext = [];
      return SevenZipGostDecryptResult.InvalidData;
    }

    if (!SevenZipGostCoder.IsSupportedNumCyclesPower(properties.NumCyclesPower))
    {
      plaintext = [];
      return SevenZipGostDecryptResult.NotSupported;
    }

    bool directKey = properties.NumCyclesPower == SevenZipGostCoder.DirectKeyNumCyclesPower;

    Span<byte> key = stackalloc byte[SevenZipGostKeyDerivation.Gost256KeySize];

    try
    {
      bool derived = directKey
          ? SevenZipGostKeyDerivation.TryDeriveDirectKey(properties, password, key)
          : SevenZipGostKeyDerivation.TryDeriveStribogKey(properties, password, key);

      if (!derived)
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
      if (!SevenZipGostInitializationVector.TryBuildMagmaCtr(
          properties,
          out byte[] initializationVector))
      {
        plaintext = [];
        return SevenZipGostDecryptResult.InvalidData;
      }

      try
      {
        if (!GostMagmaCtrTransform.TryTransform(
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

    plaintext = [];
    return SevenZipGostDecryptResult.InvalidData;
  }
}
