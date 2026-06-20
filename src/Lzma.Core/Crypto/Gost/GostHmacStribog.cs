namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// HMAC на основе хеш-функции Стрибог (HMAC_GOSTR3411_2012_256/512, RFC 7836 §4.1).
/// </summary>
/// <remarks>
/// Стандартный HMAC (RFC 2104) с блоком B = 64 байта; в качестве хеша используется
/// Стрибог-256 (вывод 32 байта) или Стрибог-512 (вывод 64 байта). Проверено официальными
/// тест-векторами RFC 7836, Appendix B.
/// <para>
/// ВАЖНО про порядок байт: RFC 6986 представляет хеш Стрибога big-endian (так его выдаёт
/// <see cref="GostStribog"/>), а протокольные конструкции RFC 7836 (HMAC/KDF) трактуют
/// октетные строки как little-endian. Поэтому здесь хеш применяется в little-endian-октетной
/// конвенции: вход и выход разворачиваются вокруг <see cref="GostStribog"/>.
/// </para>
/// </remarks>
public static class GostHmacStribog
{
  private const int BlockSize = 64;
  private const byte IPad = 0x36;
  private const byte OPad = 0x5c;

  /// <summary>HMAC_GOSTR3411_2012_256 (вывод 32 байта).</summary>
  public static byte[] Compute256(ReadOnlySpan<byte> key, ReadOnlySpan<byte> message)
      => Compute(key, message, is512: false);

  /// <summary>HMAC_GOSTR3411_2012_512 (вывод 64 байта).</summary>
  public static byte[] Compute512(ReadOnlySpan<byte> key, ReadOnlySpan<byte> message)
      => Compute(key, message, is512: true);

  private static byte[] Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> message, bool is512)
  {
    // K0: ключ приводится к размеру блока B. Длиннее B — хешируется, затем дополняется нулями;
    // короче или равен — дополняется нулями (RFC 2104).
    Span<byte> k0 = stackalloc byte[BlockSize];
    if (key.Length > BlockSize)
      Hash(key, is512).CopyTo(k0);
    else
      key.CopyTo(k0);

    // inner = H((K0 xor ipad) || message)
    byte[] inner = new byte[BlockSize + message.Length];
    for (int i = 0; i < BlockSize; i++)
      inner[i] = (byte)(k0[i] ^ IPad);
    message.CopyTo(inner.AsSpan(BlockSize));

    byte[] innerHash = Hash(inner, is512);

    // outer = H((K0 xor opad) || innerHash)
    byte[] outer = new byte[BlockSize + innerHash.Length];
    for (int i = 0; i < BlockSize; i++)
      outer[i] = (byte)(k0[i] ^ OPad);
    innerHash.CopyTo(outer.AsSpan(BlockSize));

    return Hash(outer, is512);
  }

  // Хеш в little-endian-октетной конвенции RFC 7836: разворачиваем вход и выход вокруг
  // big-endian Стрибога (GostStribog соответствует представлению RFC 6986).
  private static byte[] Hash(ReadOnlySpan<byte> data, bool is512)
  {
    byte[] input = data.ToArray();
    Array.Reverse(input);

    byte[] hash = is512 ? GostStribog.Hash512(input) : GostStribog.Hash256(input);
    Array.Reverse(hash);

    return hash;
  }
}
