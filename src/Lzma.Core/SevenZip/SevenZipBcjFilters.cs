using System.Buffers.Binary;

namespace Lzma.Core.SevenZip;

/// <summary>
/// BCJ-фильтры ветвлений (branch converters) для 7z: decode-преобразования на месте.
/// </summary>
/// <remarks>
/// Порты соответствующих конвертеров из LZMA SDK (Bra.c / Bra86.c / BraIA64.c).
/// Все методы выполняют decode-направление (абсолютные адреса → относительные смещения)
/// и работают прямо в переданном буфере. <c>startOffset</c> — виртуальный стартовый offset
/// (обычно 0); при покусочной обработке его нужно накапливать.
/// </remarks>
internal static class SevenZipBcjFilters
{
  public static void ArmDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): ARM_Convert(data, size, ip, encoding=0).
    // Обрабатывает только выровненные по 4 байтам инструкции.
    // Патчит BL-инструкции (последний байт == 0xEB), переводя абсолют -> относительный.
    //
    // startOffset — виртуальный offset для ip (обычно 0). Если фильтр вызывается кусками,
    // ip надо накапливать, но у нас сейчас decode целого буфера.

    int size = data.Length & ~3;            // size &= ~(size_t)3
    uint ip = unchecked(startOffset + 4u);  // ip += 4

    for (int i = 0; i + 4 <= size; i += 4)
    {
      if (data[i + 3] != 0xEB)
        continue;

      uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4));

      v <<= 2;
      // В оригинале используется (p - data), где p уже сдвинут на +4.
      // То есть добавляется (i + 4).
      v = unchecked(v - (ip + (uint)(i + 4)));
      v >>= 2;

      v &= 0x00FFFFFF;
      v |= 0xEB000000;

      BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), v);
    }
  }

  public static void ArmtDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (Bra.c): ARMT_Convert(data, size, ip, encoding=0).
    // Обрабатывает Thumb-2 BL-последовательности.
    if (data.Length < 4)
      return;

    int limit = data.Length - 4;            // size -= 4
    uint ip = unchecked(startOffset + 4u);  // ip += 4

    for (int i = 0; i <= limit; i += 2)
      if ((data[i + 1] & 0xF8) == 0xF0 &&
                 (data[i + 3] & 0xF8) == 0xF8)
      {
        uint src =
          ((data[i + 1] & 0x7u) << 19) |
          ((uint)data[i + 0] << 11) |
          ((data[i + 3] & 0x7u) << 8) |
          data[i + 2];

        src <<= 1;

        uint dest = unchecked(src - (ip + (uint)i));
        dest >>= 1;

        data[i + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
        data[i + 0] = (byte)(dest >> 11);
        data[i + 3] = (byte)(0xF8 | ((dest >> 8) & 0x7));
        data[i + 2] = (byte)dest;

        i += 2; // как в оригинале: внутри for делается i += 2
      }
  }

  public static void PpcDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): PPC_Convert(data, size, ip, encoding=0).
    // Работает по 4-байтным инструкциям, big-endian.
    if (data.Length < 4)
      return;

    int limit = data.Length - 4;

    for (int i = 0; i <= limit; i += 4)
    {
      // Условие из Bra.c:
      // (data[i] >> 2) == 0x12  => 0x48..0x4B
      // (data[i + 3] & 3) == 1
      if ((data[i] >> 2) != 0x12 || (data[i + 3] & 3) != 1)
        continue;

      uint src =
        ((uint)(data[i + 0] & 3) << 24) |
        ((uint)data[i + 1] << 16) |
        ((uint)data[i + 2] << 8) |
        ((uint)data[i + 3] & 0xFFFFFFFCu);

      uint dest = unchecked(src - (startOffset + (uint)i));

      data[i + 0] = (byte)(0x48 | ((dest >> 24) & 0x3));
      data[i + 1] = (byte)(dest >> 16);
      data[i + 2] = (byte)(dest >> 8);

      byte b3 = data[i + 3];
      b3 &= 0x3;
      b3 |= (byte)dest;
      data[i + 3] = b3;
    }
  }

  public static void X86DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra86.c): x86 BCJ decoder.
    // Декодирование: абсолютные адреса -> относительные смещения (для E8/E9).
    //
    // startOffset — виртуальный стартовый оффсет (обычно 0). В 7z чаще всего props нет.
    // В формуле используется ip = startOffset + 5, чтобы соответствовать (pos + 5).

    static bool Test86MSByte(byte b) => b == 0 || b == 0xFF;

    ReadOnlySpan<byte> kMaskToAllowedStatus = [1, 1, 1, 0, 1, 0, 0, 0];
    ReadOnlySpan<byte> kMaskToBitNumber = [0, 1, 2, 2, 3, 3, 3, 3];

    if (data.Length < 5)
      return;

    uint ip = unchecked(startOffset + 5u);

    int bufferPos = 0;
    int prevPos = -1;
    uint prevMask = 0;

    while (true)
    {
      int limit = data.Length - 4;
      int p = bufferPos;

      // Ищем следующий E8/E9 (CALL/JMP near).
      for (; p < limit; p++)
        if ((data[p] & 0xFE) == 0xE8)
          break;

      bufferPos = p;
      if (p >= limit)
        break;

      int distance = bufferPos - prevPos;
      if (distance > 3)
        prevMask = 0;
      else
      {
        prevMask = (prevMask << (distance - 1)) & 0x7;

        if (prevMask != 0)
        {
          byte b = data[bufferPos + 4 - kMaskToBitNumber[(int)prevMask]];

          if (kMaskToAllowedStatus[(int)prevMask] == 0 || Test86MSByte(b))
          {
            prevPos = bufferPos;
            prevMask = ((prevMask << 1) & 0x7) | 1u;
            bufferPos++;
            continue;
          }
        }
      }

      prevPos = bufferPos;

      if (Test86MSByte(data[bufferPos + 4]))
      {
        uint src =
          ((uint)data[bufferPos + 4] << 24) |
          ((uint)data[bufferPos + 3] << 16) |
          ((uint)data[bufferPos + 2] << 8) |
          data[bufferPos + 1];

        uint dest;

        while (true)
        {
          // decode: rel = abs - (ip + pos)
          dest = unchecked(src - (ip + (uint)bufferPos));

          if (prevMask == 0)
            break;

          int bIndex = kMaskToBitNumber[(int)prevMask] * 8;
          byte b = (byte)(dest >> (24 - bIndex));

          if (!Test86MSByte(b))
            break;

          src = dest ^ ((1u << (32 - bIndex)) - 1u);
        }

        data[bufferPos + 4] = (byte)(~(((dest >> 24) & 1) - 1));
        data[bufferPos + 3] = (byte)(dest >> 16);
        data[bufferPos + 2] = (byte)(dest >> 8);
        data[bufferPos + 1] = (byte)dest;

        bufferPos += 5;
      }
      else
      {
        prevMask = ((prevMask << 1) & 0x7) | 1u;
        bufferPos++;
      }
    }
  }

  public static void SparcDecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (Bra.c): SPARC_Convert(data, size, ip, encoding=0).
    // Big-endian, обрабатывает только выровненные по 4 байта инструкции.
    int size = data.Length & ~3;

    for (int i = 0; i + 4 <= size; i += 4)
    {
      byte b0 = data[i];
      byte b1 = data[i + 1];

      // Условие из Bra.c (упрощённая проверка ветвления).
      if (!((b0 == 0x40 && (b1 & 0xC0) == 0) || (b0 == 0x7F && b1 >= 0xC0)))
        continue;

      uint v = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4));

      v <<= 2;

      // В Bra.c: ip -= 4; используется ip + (p - data), где p уже сдвинут на +4.
      // Это эквивалентно (startOffset + i).
      v = unchecked(v - (startOffset + (uint)i));

      v &= 0x01FFFFFFu;
      v = unchecked(v - (1u << 24));
      v ^= 0xFF000000u;
      v >>= 2;
      v |= 0x40000000u;

      BinaryPrimitives.WriteUInt32BigEndian(data.Slice(i, 4), v);
    }
  }

  public static void Ia64DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Порт из LZMA SDK (BraIA64.c): IA64_Convert(data, size, ip, encoding=0).
    // Обрабатываются только полные 16-байтные bundle’ы; хвост < 16 остаётся как есть.

    if (data.Length < 16)
      return;

    int lastBundleStart = data.Length - 16;

    for (int i = 0; i <= lastBundleStart; i += 16)
    {
      int m = (int)((0x334B0000u >> (data[i] & 0x1E)) & 3u);
      if (m == 0)
        continue;

      // В оригинале: m++; do { ... } while (++m <= 4);
      for (++m; m <= 4; m++)
      {
        int p = i + m * 5 - 8;

        // if (((p[3] >> m) & 15) == 5 ...
        if (((data[p + 3] >> m) & 0xF) != 5)
          continue;

        // && (((p[-1] | ((UInt32)p[0] << 8)) >> m) & 0x70) == 0)
        uint t = (uint)(data[p - 1] | (data[p] << 8));
        if (((t >> m) & 0x70u) != 0)
          continue;

        uint raw = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(p, 4));

        uint v = raw >> m;
        v = (v & 0xFFFFFu) | ((v & (1u << 23)) >> 3);
        v <<= 4;

        uint add = unchecked(startOffset + (uint)i);
        v = unchecked(v - add);

        v >>= 4;
        v &= 0x1FFFFFu;
        v = unchecked(v + 0x700000u);
        v &= 0x8FFFFFu;

        raw &= ~(0x8FFFFFu << m);
        raw |= (v << m);

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(p, 4), raw);
      }
    }
  }

  public static void Arm64DecodeInPlace(Span<byte> data, uint startOffset)
  {
    // Port из LZMA SDK (Bra.c): z7_BranchConv_ARM64_Dec.
    // Обрабатывает только выровненные по 4 байтам инструкции.
    // startOffset — виртуальный offset (обычно 0). Если фильтр вызван кусками, его нужно накапливать.

    int size = data.Length & ~3;

    const uint flag = 1u << (24 - 4);            // 1 << 20
    const uint mask = (1u << 24) - (flag << 1);  // 0x00E00000

    for (int i = 0; i + 4 <= size; i += 4)
    {
      uint v = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i, 4));
      uint pc = unchecked(startOffset + (uint)i);

      // BL imm26 (0x94xxxxxx)
      if (((v - 0x94000000u) & 0xFC000000u) == 0u)
      {
        uint c = pc >> 2;
        v = unchecked(v - c);
        v &= 0x03FFFFFFu;
        v |= 0x94000000u;

        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), v);
        continue;
      }

      // ADRP-подобный паттерн (см. Bra.c)
      v = unchecked(v - 0x90000000u);
      if ((v & 0x9F000000u) != 0u)
        continue;

      v = unchecked(v + flag);
      if ((v & mask) != 0u)
        continue;

      uint z = (v & 0xFFFFFFE0u) | (v >> 26);

      uint c2 = (pc >> (12 - 3)) & ~7u; // (pc >> 9) & ~7
      z = unchecked(z - c2);

      uint outV = v & 0x1Fu;
      outV |= 0x90000000u;
      outV |= z << 26;
      outV |= 0x00FFFFE0u & unchecked((z & ((flag << 1) - 1)) - flag);

      BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(i, 4), outV);
    }
  }
}
