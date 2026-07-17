using System.Numerics;
using System.Security.Cryptography;

namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Низкоуровневая реализация блочного шифра Магма (ГОСТ Р 34.12-2015, 64-битный блок).
/// </summary>
/// <remarks>
/// Реализация опирается на RFC 8891 / ГОСТ Р 34.12-2015. Поддерживаются шифрование и
/// дешифрование одного блока. Блок и ключ трактуются big-endian, как в стандарте.
/// </remarks>
public static class GostMagmaCipher
{
  /// <summary>Размер блока в байтах (64 бита).</summary>
  public const int BlockSize = 8;

  /// <summary>Размер ключа в байтах (256 бит).</summary>
  public const int KeySize = 32;

  // Узел замены Pi' (id-tc26-gost-28147-param-Z), RFC 8891 §4.1: Pi[i] применяется к i-му
  // полубайту (i = 0 — младший).
  private static readonly byte[][] Pi =
  [
    [12, 4, 6, 2, 10, 5, 11, 9, 14, 8, 13, 7, 0, 3, 15, 1],
    [6, 8, 2, 3, 9, 10, 5, 12, 1, 14, 4, 7, 11, 13, 0, 15],
    [11, 3, 5, 8, 2, 15, 10, 13, 14, 1, 7, 4, 12, 9, 6, 0],
    [12, 8, 2, 1, 13, 4, 15, 6, 7, 0, 10, 5, 3, 14, 9, 11],
    [7, 15, 5, 10, 8, 1, 6, 13, 0, 9, 3, 14, 11, 4, 2, 12],
    [5, 13, 15, 6, 9, 2, 12, 10, 11, 7, 8, 1, 4, 3, 14, 0],
    [8, 14, 2, 5, 6, 9, 1, 12, 15, 4, 11, 0, 13, 10, 3, 7],
    [1, 7, 14, 13, 0, 5, 8, 3, 4, 15, 10, 6, 9, 12, 11, 2],
  ];

  /// <summary>Пытается зашифровать один блок.</summary>
  public static bool TryEncryptBlock(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> plaintext,
      Span<byte> ciphertext)
  {
    if (key.Length != KeySize || plaintext.Length != BlockSize || ciphertext.Length < BlockSize)
      return false;

    Span<uint> roundKeys = stackalloc uint[32];
    ExpandRoundKeys(key, roundKeys);

    try
    {
      EncryptBlock(roundKeys, plaintext, ciphertext);
      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(MemoryMarshalAsBytes(roundKeys));
    }
  }

  /// <summary>
  /// Шифрует один блок УЖЕ развёрнутыми раундовыми ключами — без пересчёта расписания. Используется
  /// CTR-режимом (расписание один раз на всё преобразование, а не на каждый блок). Размеры — на вызывающем.
  /// </summary>
  internal static void EncryptBlock(ReadOnlySpan<uint> roundKeys, ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
  {
    // (a1, a0): a1 — старшие 32 бита блока, a0 — младшие (big-endian).
    uint a1 = ReadBigEndian(plaintext);
    uint a0 = ReadBigEndian(plaintext[4..]);

    // 31 раунд G[K_1..K_31] + финальный G*[K_32] (без перестановки половин).
    for (int r = 0; r < 31; r++)
    {
      uint next = G(a0, roundKeys[r]) ^ a1;
      a1 = a0;
      a0 = next;
    }

    uint high = G(a0, roundKeys[31]) ^ a1;

    WriteBigEndian(ciphertext, high);
    WriteBigEndian(ciphertext[4..], a0);
  }

  /// <summary>Пытается расшифровать один блок.</summary>
  public static bool TryDecryptBlock(
      ReadOnlySpan<byte> key,
      ReadOnlySpan<byte> ciphertext,
      Span<byte> plaintext)
  {
    if (key.Length != KeySize || ciphertext.Length != BlockSize || plaintext.Length < BlockSize)
      return false;

    Span<uint> roundKeys = stackalloc uint[32];
    ExpandRoundKeys(key, roundKeys);

    try
    {
      uint a1 = ReadBigEndian(ciphertext);
      uint a0 = ReadBigEndian(ciphertext[4..]);

      // Дешифрование — те же раунды, но с обратным порядком ключей: G[K_32..K_2] + G*[K_1].
      for (int r = 31; r >= 1; r--)
      {
        uint next = G(a0, roundKeys[r]) ^ a1;
        a1 = a0;
        a0 = next;
      }

      uint high = G(a0, roundKeys[0]) ^ a1;

      WriteBigEndian(plaintext, high);
      WriteBigEndian(plaintext[4..], a0);

      return true;
    }
    finally
    {
      CryptographicOperations.ZeroMemory(MemoryMarshalAsBytes(roundKeys));
    }
  }

  /// <summary>
  /// Разворачивает 256-битный ключ в 32 раундовых ключа (RFC 8891 §4.3): K_1..K_8 —
  /// big-endian 32-битные слова ключа; K_9..K_24 повторяют K_1..K_8; K_25..K_32 — K_8..K_1.
  /// </summary>
  internal static void ExpandRoundKeys(ReadOnlySpan<byte> key, Span<uint> roundKeys)
  {
    for (int i = 0; i < 8; i++)
    {
      uint k = ReadBigEndian(key[(i * 4)..]);
      roundKeys[i] = k;       // K_1..K_8
      roundKeys[i + 8] = k;   // K_9..K_16
      roundKeys[i + 16] = k;  // K_17..K_24
      roundKeys[31 - i] = k;  // K_25..K_32 = K_8..K_1
    }
  }

  // g[k](a) = t((a + k) mod 2^32) <<<_11.
  private static uint G(uint a, uint k) => BitOperations.RotateLeft(T(unchecked(a + k)), 11);

  // t: применяет узел замены к каждому полубайту.
  private static uint T(uint a)
  {
    uint result = 0;
    for (int i = 0; i < 8; i++)
    {
      int nibble = (int)((a >> (i * 4)) & 0xF);
      result |= (uint)Pi[i][nibble] << (i * 4);
    }

    return result;
  }

  private static uint ReadBigEndian(ReadOnlySpan<byte> source)
      => ((uint)source[0] << 24) | ((uint)source[1] << 16) | ((uint)source[2] << 8) | source[3];

  private static void WriteBigEndian(Span<byte> destination, uint value)
  {
    destination[0] = (byte)(value >> 24);
    destination[1] = (byte)(value >> 16);
    destination[2] = (byte)(value >> 8);
    destination[3] = (byte)value;
  }

  private static Span<byte> MemoryMarshalAsBytes(Span<uint> values)
      => System.Runtime.InteropServices.MemoryMarshal.AsBytes(values);
}
