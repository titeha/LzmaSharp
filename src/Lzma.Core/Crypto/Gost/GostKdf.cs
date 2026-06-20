namespace Lzma.Core.Crypto.Gost;

/// <summary>
/// Функция формирования ключа KDF_GOSTR3411_2012_256 (RFC 7836 §4.5).
/// </summary>
/// <remarks>
/// KDF_GOSTR3411_2012_256(K_in, label, seed) =
/// HMAC_GOSTR3411_2012_256(K_in, 0x01 | label | 0x00 | seed | 0x01 | 0x00).
/// Вывод — 32 байта. Проверено официальным тест-вектором RFC 7836.
/// </remarks>
public static class GostKdf
{
  /// <summary>Размер вырабатываемого ключа в байтах.</summary>
  public const int OutputSize = 32;

  /// <summary>
  /// Вырабатывает 256-битный ключ из ключа <paramref name="keyIn"/>, метки
  /// <paramref name="label"/> и затравки <paramref name="seed"/>.
  /// </summary>
  public static byte[] Derive256(ReadOnlySpan<byte> keyIn, ReadOnlySpan<byte> label, ReadOnlySpan<byte> seed)
  {
    // message = 0x01 || label || 0x00 || seed || 0x01 || 0x00
    byte[] message = new byte[1 + label.Length + 1 + seed.Length + 2];
    int offset = 0;

    message[offset++] = 0x01;
    label.CopyTo(message.AsSpan(offset));
    offset += label.Length;
    message[offset++] = 0x00;
    seed.CopyTo(message.AsSpan(offset));
    offset += seed.Length;
    message[offset++] = 0x01;
    message[offset] = 0x00;

    return GostHmacStribog.Compute256(keyIn, message);
  }
}
