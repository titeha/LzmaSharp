using SevenZip;

namespace LzmaCore.Tests;

internal static class UpstreamLzmaSdk
{
  public static byte[] EncodeLzmaAlone(byte[] data, int dictionary = 1 << 20)
  {
    using var inStream = new MemoryStream(data, writable: false);
    using var outStream = new MemoryStream();

    // Настройки близкие к дефолтным LZMA (как в LzmaAlone из SDK).
    var propIDs = new[]
    {
            CoderPropID.DictionarySize,
            CoderPropID.PosStateBits,
            CoderPropID.LitContextBits,
            CoderPropID.LitPosBits,
            CoderPropID.Algorithm,
            CoderPropID.NumFastBytes,
            CoderPropID.MatchFinder,
            CoderPropID.EndMarker
        };

    object[] properties =
    [
            dictionary,
            2,   // posStateBits (pb)
            3,   // litContextBits (lc)
            0,   // litPosBits (lp)
            2,   // algorithm
            128, // numFastBytes
            "bt4",
            false // end marker
        ];

    var encoder = new global::SevenZip.Compression.LZMA.Encoder();
    encoder.SetCoderProperties(propIDs, properties);

    // LZMA-Alone header:
    // 5 байт props (1 байт lc/lp/pb + 4 байта dictionary)
    // 8 байт uncompressed size (Int64 little endian)
    encoder.WriteCoderProperties(outStream);

    long fileSize = data.Length;
    for (int i = 0; i < 8; i++)
      outStream.WriteByte((byte)(fileSize >> (8 * i)));

    encoder.Code(inStream, outStream, inStream.Length, -1, null!);

    return outStream.ToArray();
  }

  public static byte[] WrapLzmaAlonePayloadIntoLzma2(byte[] lzmaAlone, int unpackSize)
  {
    if (lzmaAlone.Length < 13)
      throw new ArgumentException("Слишком короткий LZMA-Alone поток.", nameof(lzmaAlone));

    // Первый байт props (lc/lp/pb) — пишется в LZMA2, если control содержит флаг 0x10 (new props).
    byte lzmaPropsByte = lzmaAlone[0];

    // LZMA payload начинается после 13-байтного заголовка.
    const int payloadOffset = 13;
    int packSize = lzmaAlone.Length - payloadOffset;

    if (packSize <= 0)
      throw new ArgumentException("Пустой LZMA payload.", nameof(lzmaAlone));

    if (packSize > 0x10000)
      throw new ArgumentException("Для одного LZMA2-чанка packSize должен быть <= 65536. Уменьши входные данные.", nameof(lzmaAlone));

    if (unpackSize <= 0 || unpackSize > 1 << 21)
      throw new ArgumentOutOfRangeException(nameof(unpackSize), "Для одного LZMA2-чанка unpackSize должен быть 1..~2MiB.");

    // Кодируем unpackSize-1 в 3 байта (верхние 5 бит в control, ещё 2 байта отдельно).
    int unpackMinus1 = unpackSize - 1;
    int packMinus1 = packSize - 1;

    // 0xE0 = LZMA + reset dic + reset state + новые props (0x40).
    byte control = (byte)(0xE0 | ((unpackMinus1 >> 16) & 0x1F));
    byte u0 = (byte)((unpackMinus1 >> 8) & 0xFF);
    byte u1 = (byte)(unpackMinus1 & 0xFF);

    byte p0 = (byte)((packMinus1 >> 8) & 0xFF);
    byte p1 = (byte)(packMinus1 & 0xFF);

    // Формат LZMA2: [control][u0][u1][p0][p1][propsByte][payload...][0x00 end]
    byte[] outBuf = new byte[1 + 2 + 2 + 1 + packSize + 1];

    int pos = 0;
    outBuf[pos++] = control;
    outBuf[pos++] = u0;
    outBuf[pos++] = u1;
    outBuf[pos++] = p0;
    outBuf[pos++] = p1;
    outBuf[pos++] = lzmaPropsByte;

    Buffer.BlockCopy(lzmaAlone, payloadOffset, outBuf, pos, packSize);
    pos += packSize;

    outBuf[pos++] = 0x00; // end marker

    return outBuf;
  }
}
