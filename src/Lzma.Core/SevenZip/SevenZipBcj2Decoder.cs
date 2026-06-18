namespace Lzma.Core.SevenZip;

/// <summary>
/// Слияние четырёх потоков BCJ2 в финальный выход (range-decoder + конвертация ABS→REL).
/// </summary>
/// <remarks>
/// BCJ2 разбивает x86-поток на 4 части: основной (buf0), disp32 для E8 (buf1),
/// disp32 для E9/Jcc (buf2) и управляющий range-stream (buf3). Здесь они объединяются
/// обратно в исходный поток. Публичная точка входа — <see cref="SevenZipFolderDecoder.TryDecodeBcj2ToArray"/>.
/// </remarks>
internal static class SevenZipBcj2Decoder
{
  public static SevenZipFolderDecodeResult DecodeToArray(
  ReadOnlySpan<byte> buf0,
  ReadOnlySpan<byte> buf1,
  ReadOnlySpan<byte> buf2,
  ReadOnlySpan<byte> buf3,
  int outSize,
  out byte[] output)
  {
    output = [];

    if (outSize < 0)
      return SevenZipFolderDecodeResult.InvalidData;

    if (outSize == 0)
    {
      output = [];
      return SevenZipFolderDecodeResult.Ok;
    }

    const int kNumBitModelTotalBits = 11;
    const uint kBitModelTotal = 1u << kNumBitModelTotalBits; // 2048

    const int kNumMoveBits = 5;

    static bool IsJcc(byte b0, byte b1) => b0 == 0x0F && (b1 & 0xF0) == 0x80;
    static bool IsJ(byte b0, byte b1) => (b1 & 0xFE) == 0xE8 || IsJcc(b0, b1);

    output = new byte[outSize];

    // prob модели: 256 для E8 (индекс по prevByte) + 2 общих (E9 и Jcc/прочее)
    uint[] p = new uint[256 + 2];
    for (int i = 0; i < p.Length; i++)
      p[i] = kBitModelTotal >> 1;

    int inPos = 0;
    int outPos = 0;

    int pos1 = 0;
    int pos2 = 0;
    int pos3 = 0;

    byte prevByte = 0;

    // Инициализация range decoder: 5 байт из buf3
    if (buf3.Length < 5)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    uint code = 0;
    for (int i = 0; i < 5; i++)
      code = (code << 8) | buf3[pos3++];

    uint range = 0xFFFF_FFFFu;

    try
    {
      for (; ; )
      {
        int limit = buf0.Length - inPos;
        int outRemain = outSize - outPos;
        if (outRemain < limit)
          limit = outRemain;

        while (limit != 0)
        {
          byte b = buf0[inPos];
          output[outPos++] = b;

          if (IsJ(prevByte, b))
            break;

          inPos++;
          prevByte = b;
          limit--;
        }

        if (limit == 0 || outPos == outSize)
          break;

        // b = байт перехода/второй байт Jcc (он уже записан в output ранее)
        byte b2 = buf0[inPos++];

        int probIndex;
        if (b2 == 0xE8)
          probIndex = prevByte; // 0..255
        else if (b2 == 0xE9)
          probIndex = 256;
        else
          probIndex = 257;

        uint ttt = p[probIndex];
        uint bound = (range >> kNumBitModelTotalBits) * ttt;

        if (code < bound)
        {
          // BIT = 0: disp32 остаётся в buf0 (ничего не подмешиваем)
          range = bound;
          p[probIndex] = ttt + ((kBitModelTotal - ttt) >> kNumMoveBits);
          if (!Normalize(buf3, ref pos3, ref range, ref code))
          {
            output = [];
            return SevenZipFolderDecodeResult.InvalidData;
          }
          prevByte = b2;
        }
        else
        {
          // BIT = 1: disp32 берём из buf1/buf2 и конвертируем ABS->REL
          range -= bound;
          code -= bound;
          p[probIndex] = ttt - (ttt >> kNumMoveBits);
          if (!Normalize(buf3, ref pos3, ref range, ref code))
          {
            output = [];
            return SevenZipFolderDecodeResult.InvalidData;
          }

          uint abs;
          if (b2 == 0xE8)
          {
            if (pos1 + 4 > buf1.Length)
            {
              output = [];
              return SevenZipFolderDecodeResult.InvalidData;
            }

            abs = ((uint)buf1[pos1] << 24) |
                  ((uint)buf1[pos1 + 1] << 16) |
                  ((uint)buf1[pos1 + 2] << 8) |
                  ((uint)buf1[pos1 + 3]);
            pos1 += 4;
          }
          else
          {
            if (pos2 + 4 > buf2.Length)
            {
              output = [];
              return SevenZipFolderDecodeResult.InvalidData;
            }

            abs = ((uint)buf2[pos2] << 24) |
                  ((uint)buf2[pos2 + 1] << 16) |
                  ((uint)buf2[pos2 + 2] << 8) |
                  ((uint)buf2[pos2 + 3]);
            pos2 += 4;
          }

          uint dest = unchecked(abs - (uint)(outPos + 4));

          output[outPos++] = (byte)dest;
          if (outPos == outSize)
            break;

          output[outPos++] = (byte)(dest >> 8);
          if (outPos == outSize)
            break;

          output[outPos++] = (byte)(dest >> 16);
          if (outPos == outSize)
            break;

          output[outPos++] = prevByte = (byte)(dest >> 24);
        }
      }
    }
    catch (InvalidOperationException)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    if (outPos != outSize)
    {
      output = [];
      return SevenZipFolderDecodeResult.InvalidData;
    }

    return SevenZipFolderDecodeResult.Ok;
  }

  private static bool Normalize(ReadOnlySpan<byte> rangeStream, ref int pos, ref uint range, ref uint code)
  {
    const uint kTopValue = 1u << 24;

    if (range >= kTopValue)
      return true;

    if ((uint)pos >= (uint)rangeStream.Length)
      return false;

    range <<= 8;
    code = (code << 8) | rangeStream[pos++];
    return true;
  }
}
