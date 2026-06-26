using System;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Описание 7zAES coder-а.
/// </summary>
public static class SevenZipAesCoder
{
  /// <summary>
  /// Максимальный поддерживаемый показатель числа циклов derivation,
  /// который используется реализацией 7-Zip.
  /// </summary>
  public const byte SupportedNumCyclesPowerMax = 24;

  /// <summary>
  /// Специальное значение 7zAES, при котором ключ строится напрямую
  /// из salt и password без обычного SHA-256 цикла.
  /// </summary>
  public const byte DirectKeyNumCyclesPower = 0x3F;

  /// <summary>
  /// Максимальный размер salt в свойствах 7zAES.
  /// </summary>
  public const int MaxSaltSize = 16;

  /// <summary>
  /// Максимальный размер IV в свойствах 7zAES.
  /// </summary>
  public const int MaxInitializationVectorSize = 16;

  /// <summary>
  /// Проверяет, является ли method id идентификатором 7zAES.
  /// </summary>
  public static bool IsAesMethodId(ReadOnlySpan<byte> methodId)
  {
    return methodId.Length == 4
        && methodId[0] == 0x06
        && methodId[1] == 0xF1
        && methodId[2] == 0x07
        && methodId[3] == 0x01;
  }

  /// <summary>
  /// Проверяет, поддерживается ли показатель числа циклов derivation.
  /// </summary>
  public static bool IsSupportedNumCyclesPower(byte numCyclesPower)
  {
    return numCyclesPower <= SupportedNumCyclesPowerMax
        || numCyclesPower == DirectKeyNumCyclesPower;
  }

  /// <summary>
  /// Сериализует свойства 7zAES coder-а — инверсия <see cref="TryParseProperties"/>.
  /// </summary>
  public static bool TrySerializeProperties(
      SevenZipAesProperties properties,
      out byte[] serialized)
  {
    ArgumentNullException.ThrowIfNull(properties);

    serialized = [];

    if (properties.NumCyclesPower > 0x3F)
      return false;

    int saltSize = properties.Salt.Length;
    int ivSize = properties.InitializationVector.Length;

    if (saltSize > MaxSaltSize || ivSize > MaxInitializationVectorSize)
      return false;

    byte b0 = (byte)(properties.NumCyclesPower
        | (saltSize > 0 ? 0x80 : 0)
        | (ivSize > 0 ? 0x40 : 0));

    if (saltSize == 0 && ivSize == 0)
    {
      serialized = [b0];
      return true;
    }

    // saltSize кодируется как старший бит в b0 + (saltSize-1) в старшем ниббле b1; аналогично ivSize.
    byte b1 = (byte)(((saltSize > 0 ? saltSize - 1 : 0) << 4)
        | (ivSize > 0 ? ivSize - 1 : 0));

    byte[] result = new byte[2 + saltSize + ivSize];
    result[0] = b0;
    result[1] = b1;
    properties.Salt.CopyTo(result.AsSpan(2, saltSize));
    properties.InitializationVector.CopyTo(result.AsSpan(2 + saltSize, ivSize));

    serialized = result;
    return true;
  }

  /// <summary>
  /// Пытается разобрать свойства 7zAES coder-а.
  /// </summary>
  public static bool TryParseProperties(
      ReadOnlySpan<byte> properties,
      out SevenZipAesProperties? parsed)
  {
    parsed = null;

    if (properties.Length == 0)
    {
      parsed = new SevenZipAesProperties(
          numCyclesPower: 0,
          salt: [],
          initializationVector: []);

      return true;
    }

    byte b0 = properties[0];
    byte numCyclesPower = (byte)(b0 & 0x3F);

    if ((b0 & 0xC0) == 0)
    {
      if (properties.Length != 1)
        return false;

      parsed = new SevenZipAesProperties(
          numCyclesPower: numCyclesPower,
          salt: [],
          initializationVector: []);

      return true;
    }

    if (properties.Length <= 1)
      return false;

    byte b1 = properties[1];

    int saltSize = ((b0 >> 7) & 1) + (b1 >> 4);
    int ivSize = ((b0 >> 6) & 1) + (b1 & 0x0F);

    if (saltSize > MaxSaltSize || ivSize > MaxInitializationVectorSize)
      return false;

    int expectedSize = 2 + saltSize + ivSize;
    if (properties.Length != expectedSize)
      return false;

    byte[] salt = properties.Slice(2, saltSize).ToArray();
    byte[] iv = properties.Slice(2 + saltSize, ivSize).ToArray();

    parsed = new SevenZipAesProperties(
        numCyclesPower: numCyclesPower,
        salt: salt,
        initializationVector: iv);

    return true;
  }
}

/// <summary>
/// Разобранные свойства 7zAES coder-а.
/// </summary>
public sealed class SevenZipAesProperties
{
  /// <summary>
  /// Создаёт разобранное описание свойств 7zAES coder-а.
  /// </summary>
  public SevenZipAesProperties(
      byte numCyclesPower,
      byte[] salt,
      byte[] initializationVector)
  {
    NumCyclesPower = numCyclesPower;
    Salt = salt;
    InitializationVector = initializationVector;
  }

  /// <summary>
  /// Показатель числа циклов derivation.
  /// </summary>
  public byte NumCyclesPower { get; }

  /// <summary>
  /// Salt из свойств coder-а.
  /// </summary>
  public byte[] Salt { get; }

  /// <summary>
  /// Initialization Vector из свойств coder-а.
  /// </summary>
  public byte[] InitializationVector { get; }
}
