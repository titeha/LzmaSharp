using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Lzma.Core.Zip;

/// <summary>Результат расшифровки AES-члена ZIP.</summary>
public enum WinZipAesDecryptResult
{
  /// <summary>Успешно расшифровано и проверено.</summary>
  Ok = 0,

  /// <summary>Неверный пароль (не совпало значение проверки пароля).</summary>
  WrongPassword = 1,

  /// <summary>Данные повреждены (не совпал код аутентификации HMAC).</summary>
  Corrupt = 2,

  /// <summary>Некорректная структура зашифрованного члена.</summary>
  InvalidData = 3,
}

/// <summary>
/// <para>Уровень ЧЛЕНА WinZip-AES: сборка/разбор зашифрованного члена и дополнительного поля 0x9901.</para>
/// <para>
/// Раскладка члена: <c>[salt][passwordVerifier(2)][ciphertext][authCode(10)]</c>. Extra-поле 0x9901:
/// <c>version(2) | "AE" | strength(1) | actualMethod(2)</c>. Пишем AE-1 (version=1): реальный CRC
/// несжатых данных остаётся в заголовке (совместимо с 7-Zip). Реальный метод сжатия — в extra.
/// </para>
/// </summary>
public static class WinZipAesMember
{
  /// <summary>Версия формата AE-1 (реальный CRC хранится в заголовке).</summary>
  public const ushort VersionAe1 = 1;

  /// <summary>Версия формата AE-2 (CRC в заголовке = 0).</summary>
  public const ushort VersionAe2 = 2;

  private const int ExtraDataSize = 7;         // version(2) + "AE"(2) + strength(1) + method(2)
  private static readonly byte[] VendorAe = [0x41, 0x45]; // "AE"

  /// <summary>
  /// Собирает данные extra-поля 0x9901 (БЕЗ заголовка id/size): version | "AE" | strength | actualMethod.
  /// </summary>
  public static byte[] BuildExtraFieldData(ushort version, WinZipAes.Strength strength, ushort actualMethod)
  {
    byte[] data = new byte[ExtraDataSize];
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0, 2), version);
    data[2] = VendorAe[0];
    data[3] = VendorAe[1];
    data[4] = (byte)strength;
    BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(5, 2), actualMethod);
    return data;
  }

  /// <summary>Разбирает данные extra-поля 0x9901 (после id/size).</summary>
  public static bool TryParseExtraFieldData(
      ReadOnlySpan<byte> data, out ushort version, out WinZipAes.Strength strength, out ushort actualMethod)
  {
    version = 0;
    strength = default;
    actualMethod = 0;

    if (data.Length < ExtraDataSize)
      return false;

    version = BinaryPrimitives.ReadUInt16LittleEndian(data[..2]);
    if (data[2] != VendorAe[0] || data[3] != VendorAe[1])
      return false;

    byte strengthByte = data[4];
    if (!WinZipAes.IsValidStrength(strengthByte))
      return false;

    strength = (WinZipAes.Strength)strengthByte;
    actualMethod = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
    return true;
  }

  /// <summary>
  /// Шифрует сжатые данные члена в AES-член: <c>[salt][pwVerify][ciphertext][authCode]</c> (соль случайна).
  /// </summary>
  public static byte[] Encrypt(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> password, WinZipAes.Strength strength)
  {
    int saltSize = WinZipAes.SaltSize(strength);
    byte[] salt = new byte[saltSize];
    RandomNumberGenerator.Fill(salt);

    WinZipAes.DeriveKeys(password, salt, strength, out byte[] aesKey, out byte[] macKey, out byte[] pwVerify);

    try
    {
      byte[] ciphertext = compressed.ToArray();
      WinZipAes.CtrTransform(aesKey, ciphertext);

      byte[] authCode = WinZipAes.ComputeAuthenticationCode(macKey, ciphertext);

      byte[] member = new byte[saltSize + WinZipAes.PasswordVerifierSize + ciphertext.Length + WinZipAes.AuthenticationCodeSize];
      int pos = 0;
      salt.CopyTo(member, pos); pos += saltSize;
      pwVerify.CopyTo(member, pos); pos += WinZipAes.PasswordVerifierSize;
      ciphertext.CopyTo(member, pos); pos += ciphertext.Length;
      authCode.CopyTo(member, pos);

      return member;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(aesKey);
      CryptographicOperations.ZeroMemory(macKey);
    }
  }

  /// <summary>
  /// Расшифровывает AES-член в сжатые данные, проверяя пароль (pwVerify) и целостность (HMAC).
  /// </summary>
  public static WinZipAesDecryptResult TryDecrypt(
      ReadOnlySpan<byte> member, ReadOnlySpan<byte> password, WinZipAes.Strength strength, out byte[] compressed)
  {
    compressed = [];

    int saltSize = WinZipAes.SaltSize(strength);
    int overhead = saltSize + WinZipAes.PasswordVerifierSize + WinZipAes.AuthenticationCodeSize;
    if (member.Length < overhead)
      return WinZipAesDecryptResult.InvalidData;

    ReadOnlySpan<byte> salt = member[..saltSize];
    ReadOnlySpan<byte> pwVerify = member.Slice(saltSize, WinZipAes.PasswordVerifierSize);
    int ciphertextLength = member.Length - overhead;
    ReadOnlySpan<byte> ciphertext = member.Slice(saltSize + WinZipAes.PasswordVerifierSize, ciphertextLength);
    ReadOnlySpan<byte> authCode = member[(member.Length - WinZipAes.AuthenticationCodeSize)..];

    WinZipAes.DeriveKeys(password, salt, strength, out byte[] aesKey, out byte[] macKey, out byte[] expectedVerify);

    try
    {
      if (!CryptographicOperations.FixedTimeEquals(pwVerify, expectedVerify))
        return WinZipAesDecryptResult.WrongPassword;

      byte[] expectedAuth = WinZipAes.ComputeAuthenticationCode(macKey, ciphertext);
      if (!CryptographicOperations.FixedTimeEquals(authCode, expectedAuth))
        return WinZipAesDecryptResult.Corrupt;

      byte[] plain = ciphertext.ToArray();
      WinZipAes.CtrTransform(aesKey, plain);

      compressed = plain;
      return WinZipAesDecryptResult.Ok;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(aesKey);
      CryptographicOperations.ZeroMemory(macKey);
    }
  }
}
