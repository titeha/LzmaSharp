using System.Security.Cryptography;

namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Низкоуровневая реализация блочного шифра Кузнечик.
/// </summary>
/// <remarks>
/// Реализация опирается на RFC 7801 / ГОСТ Р 34.12-2015.
/// На этом шаге поддерживаются только шифрование и дешифрование одного блока.
/// </remarks>
public static class GostKuznyechikCipher
{
  /// <summary>
  /// Размер блока в байтах.
  /// </summary>
  public const int BlockSize = 16;

  /// <summary>
  /// Размер ключа в байтах.
  /// </summary>
  public const int KeySize = 32;

  private static readonly byte[] SBox =
  [
    0xFC, 0xEE, 0xDD, 0x11, 0xCF, 0x6E, 0x31, 0x16,
    0xFB, 0xC4, 0xFA, 0xDA, 0x23, 0xC5, 0x04, 0x4D,
    0xE9, 0x77, 0xF0, 0xDB, 0x93, 0x2E, 0x99, 0xBA,
    0x17, 0x36, 0xF1, 0xBB, 0x14, 0xCD, 0x5F, 0xC1,
    0xF9, 0x18, 0x65, 0x5A, 0xE2, 0x5C, 0xEF, 0x21,
    0x81, 0x1C, 0x3C, 0x42, 0x8B, 0x01, 0x8E, 0x4F,
    0x05, 0x84, 0x02, 0xAE, 0xE3, 0x6A, 0x8F, 0xA0,
    0x06, 0x0B, 0xED, 0x98, 0x7F, 0xD4, 0xD3, 0x1F,
    0xEB, 0x34, 0x2C, 0x51, 0xEA, 0xC8, 0x48, 0xAB,
    0xF2, 0x2A, 0x68, 0xA2, 0xFD, 0x3A, 0xCE, 0xCC,
    0xB5, 0x70, 0x0E, 0x56, 0x08, 0x0C, 0x76, 0x12,
    0xBF, 0x72, 0x13, 0x47, 0x9C, 0xB7, 0x5D, 0x87,
    0x15, 0xA1, 0x96, 0x29, 0x10, 0x7B, 0x9A, 0xC7,
    0xF3, 0x91, 0x78, 0x6F, 0x9D, 0x9E, 0xB2, 0xB1,
    0x32, 0x75, 0x19, 0x3D, 0xFF, 0x35, 0x8A, 0x7E,
    0x6D, 0x54, 0xC6, 0x80, 0xC3, 0xBD, 0x0D, 0x57,
    0xDF, 0xF5, 0x24, 0xA9, 0x3E, 0xA8, 0x43, 0xC9,
    0xD7, 0x79, 0xD6, 0xF6, 0x7C, 0x22, 0xB9, 0x03,
    0xE0, 0x0F, 0xEC, 0xDE, 0x7A, 0x94, 0xB0, 0xBC,
    0xDC, 0xE8, 0x28, 0x50, 0x4E, 0x33, 0x0A, 0x4A,
    0xA7, 0x97, 0x60, 0x73, 0x1E, 0x00, 0x62, 0x44,
    0x1A, 0xB8, 0x38, 0x82, 0x64, 0x9F, 0x26, 0x41,
    0xAD, 0x45, 0x46, 0x92, 0x27, 0x5E, 0x55, 0x2F,
    0x8C, 0xA3, 0xA5, 0x7D, 0x69, 0xD5, 0x95, 0x3B,
    0x07, 0x58, 0xB3, 0x40, 0x86, 0xAC, 0x1D, 0xF7,
    0x30, 0x37, 0x6B, 0xE4, 0x88, 0xD9, 0xE7, 0x89,
    0xE1, 0x1B, 0x83, 0x49, 0x4C, 0x3F, 0xF8, 0xFE,
    0x8D, 0x53, 0xAA, 0x90, 0xCA, 0xD8, 0x85, 0x61,
    0x20, 0x71, 0x67, 0xA4, 0x2D, 0x2B, 0x09, 0x5B,
    0xCB, 0x9B, 0x25, 0xD0, 0xBE, 0xE5, 0x6C, 0x52,
    0x59, 0xA6, 0x74, 0xD2, 0xE6, 0xF4, 0xB4, 0xC0,
    0xD1, 0x66, 0xAF, 0xC2, 0x39, 0x4B, 0x63, 0xB6,
  ];

  private static readonly byte[] _inverseSBox = BuildInverseSBox();

  private static readonly byte[] _lVector =
  [
    148, 32, 133, 16, 194, 192, 1, 251,
    1, 192, 194, 16, 133, 32, 148, 1,
  ];

  private static readonly byte[][] _roundConstants = BuildRoundConstants();

  /// <summary>
  /// Пытается зашифровать один блок.
  /// </summary>
  public static bool TryEncryptBlock(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> plaintext,
      Span<byte> ciphertext)
  {
    if (key.Length != KeySize)
      return false;

    if (plaintext.Length != BlockSize)
      return false;

    if (ciphertext.Length < BlockSize)
      return false;

    byte[][] roundKeys = ExpandRoundKeys(key);

    Span<byte> state = stackalloc byte[BlockSize];
    plaintext.CopyTo(state);

    try
    {
      for (int i = 0; i < 9; i++)
      {
        XorInPlace(state, roundKeys[i]);
        ApplySInPlace(state);
        ApplyLInPlace(state);
      }

      XorInPlace(state, roundKeys[9]);
      state.CopyTo(ciphertext);

      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(state);
      ZeroRoundKeys(roundKeys);
    }
  }

  /// <summary>
  /// Пытается расшифровать один блок.
  /// </summary>
  public static bool TryDecryptBlock(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> ciphertext,
      Span<byte> plaintext)
  {
    if (key.Length != KeySize)
      return false;

    if (ciphertext.Length != BlockSize)
      return false;

    if (plaintext.Length < BlockSize)
      return false;

    byte[][] roundKeys = ExpandRoundKeys(key);

    Span<byte> state = stackalloc byte[BlockSize];
    ciphertext.CopyTo(state);

    try
    {
      XorInPlace(state, roundKeys[9]);

      for (int i = 8; i >= 0; i--)
      {
        ApplyInverseLInPlace(state);
        ApplyInverseSInPlace(state);
        XorInPlace(state, roundKeys[i]);
      }

      state.CopyTo(plaintext);
      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(state);
      ZeroRoundKeys(roundKeys);
    }
  }

  private static byte[][] ExpandRoundKeys(ReadOnlySpan<byte> key)
  {
    var roundKeys = new byte[10][];

    roundKeys[0] = key[..BlockSize].ToArray();
    roundKeys[1] = key[BlockSize..].ToArray();

    byte[] left = [.. roundKeys[0]];
    byte[] right = [.. roundKeys[1]];

    try
    {
      for (int group = 0; group < 4; group++)
      {
        for (int j = 0; j < 8; j++)
        {
          byte[] tmp = [.. left];

          XorInPlace(tmp, _roundConstants[group * 8 + j]);
          ApplySInPlace(tmp);
          ApplyLInPlace(tmp);
          XorInPlace(tmp, right);

          right = left;
          left = tmp;
        }

        roundKeys[2 + group * 2] = [.. left];
        roundKeys[3 + group * 2] = [.. right];
      }

      return roundKeys;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(left);
      CryptographicOperations.ZeroMemory(right);
    }
  }

  private static void ApplySInPlace(Span<byte> block)
  {
    for (int i = 0; i < block.Length; i++)
      block[i] = SBox[block[i]];
  }

  private static void ApplyInverseSInPlace(Span<byte> block)
  {
    for (int i = 0; i < block.Length; i++)
      block[i] = _inverseSBox[block[i]];
  }

  private static void ApplyLInPlace(Span<byte> block)
  {
    for (int i = 0; i < 16; i++)
      ApplyRInPlace(block);
  }

  private static void ApplyInverseLInPlace(Span<byte> block)
  {
    for (int i = 0; i < 16; i++)
      ApplyInverseRInPlace(block);
  }

  private static void ApplyRInPlace(Span<byte> block)
  {
    byte x = LinearStep(block);

    for (int i = block.Length - 1; i > 0; i--)
      block[i] = block[i - 1];

    block[0] = x;
  }

  private static void ApplyInverseRInPlace(Span<byte> block)
  {
    byte last = LinearStepInverse(block);

    for (int i = 0; i < block.Length - 1; i++)
      block[i] = block[i + 1];

    block[^1] = last;
  }

  private static byte LinearStep(ReadOnlySpan<byte> block)
  {
    byte result = 0;

    for (int i = 0; i < BlockSize; i++)
      result ^= MultiplyGF(block[i], _lVector[i]);

    return result;
  }

  private static byte LinearStepInverse(ReadOnlySpan<byte> block)
  {
    Span<byte> rotated = stackalloc byte[BlockSize];

    for (int i = 0; i < BlockSize - 1; i++)
      rotated[i] = block[i + 1];

    rotated[^1] = block[0];

    return LinearStep(rotated);
  }

  private static byte MultiplyGF(byte a, byte b)
  {
    byte result = 0;
    byte x = a;
    byte y = b;

    for (int i = 0; i < 8; i++)
    {
      if ((y & 1) != 0)
        result ^= x;

      bool hi = (x & 0x80) != 0;
      x <<= 1;

      if (hi)
        x ^= 0xC3;

      y >>= 1;
    }

    return result;
  }

  private static void XorInPlace(Span<byte> destination, ReadOnlySpan<byte> source)
  {
    for (int i = 0; i < BlockSize; i++)
      destination[i] ^= source[i];
  }

  private static byte[] BuildInverseSBox()
  {
    byte[] inverse = new byte[256];

    for (int i = 0; i < 256; i++)
      inverse[SBox[i]] = (byte)i;

    return inverse;
  }

  private static byte[][] BuildRoundConstants()
  {
    var constants = new byte[32][];

    for (int i = 0; i < constants.Length; i++)
    {
      byte[] value = new byte[BlockSize];
      value[^1] = (byte)(i + 1);

      ApplyLInPlace(value);
      constants[i] = value;
    }

    return constants;
  }

  private static void ZeroRoundKeys(byte[][] roundKeys)
  {
    for (int i = 0; i < roundKeys.Length; i++)
    {
      if (roundKeys[i] is not null)
        CryptographicOperations.ZeroMemory(roundKeys[i]);
    }
  }
}
