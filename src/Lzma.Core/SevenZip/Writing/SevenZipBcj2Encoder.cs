using System.Collections.Generic;

using Lzma.Core.Lzma1;

namespace Lzma.Core.SevenZip;

/// <summary>
/// Результат BCJ2-кодирования: четыре потока, которые декодер сливает обратно в исходный x86.
/// </summary>
/// <param name="Main">Основной поток (buf0): исходные байты без вынесенных disp32.</param>
/// <param name="Call">disp32 (абсолютные, big-endian) для конвертированных E8 (buf1).</param>
/// <param name="Jump">disp32 (абсолютные, big-endian) для конвертированных E9/Jcc (buf2).</param>
/// <param name="Control">Управляющий range-stream (buf3).</param>
public readonly record struct SevenZipBcj2Streams(byte[] Main, byte[] Call, byte[] Jump, byte[] Control);

/// <summary>
/// Кодировщик BCJ2 (x86): разбивает поток на четыре части — точная инверсия
/// <see cref="SevenZipBcj2Decoder"/>. Ветвления E8/E9 и Jcc (0F 8x) с 4-байтовым смещением
/// при положительном решении конвертируются в абсолютный адрес и выносятся в отдельные потоки.
/// </summary>
/// <remarks>
/// Решение «конвертировать» повторяет эвристику эталонного 7-Zip (<c>Bcj2Enc.c</c>): рядом
/// должно быть 4 байта смещения, абсолютная цель должна попадать в пределы файла, а |смещение|
/// быть меньше <see cref="RelativeLimit"/>. Решение по каждому ветвлению фиксируется битом
/// range-кодера, поэтому декодер однозначно восстанавливает исходный поток при любой эвристике.
/// </remarks>
public static class SevenZipBcj2Encoder
{
  private const uint BitModelTotal = 1u << 11; // 2048

  /// <summary>Предел |смещения| для конвертации (как в 7-Zip: 0x0F000000 = 240 MiB).</summary>
  public const uint RelativeLimit = 0x0F00_0000u;

  private static bool IsJcc(byte b0, byte b1) => b0 == 0x0F && (b1 & 0xF0) == 0x80;
  private static bool IsBranch(byte prevByte, byte b) => (b & 0xFE) == 0xE8 || IsJcc(prevByte, b);

  /// <summary>
  /// Кодирует <paramref name="input"/> (x86) в четыре BCJ2-потока.
  /// </summary>
  public static SevenZipBcj2Streams Encode(ReadOnlySpan<byte> input)
  {
    int n = input.Length;

    var main = new List<byte>(n);
    var call = new List<byte>();
    var jump = new List<byte>();

    // prob модели: 256 для E8 (индекс по prevByte) + 2 общих (E9 и Jcc) — как в декодере.
    ushort[] p = new ushort[256 + 2];
    for (int i = 0; i < p.Length; i++)
      p[i] = (ushort)(BitModelTotal >> 1);

    // Используем проверенный LZMA range-кодер (парный к range-декодеру в SevenZipBcj2Decoder).
    var rc = new LzmaRangeEncoder();

    int pos = 0;
    byte prevByte = 0;

    while (pos < n)
    {
      byte b = input[pos];
      main.Add(b);
      pos++;

      if (!IsBranch(prevByte, b))
      {
        prevByte = b;
        continue;
      }

      // Ветвление: опкод b уже записан в main, pos указывает на начало disp (или конец).
      // Если опкод — последний байт вывода, декодер бит НЕ читает (выходит из цикла раньше).
      if (pos == n)
        break;

      int probIndex = b == 0xE8 ? prevByte : (b == 0xE9 ? 256 : 257);

      bool convert = n - pos >= 4 && ShouldConvert(b, pos, ReadLittleEndian32(input, pos), n);

      if (convert)
      {
        rc.EncodeBit(ref p[probIndex], 1);

        uint relative = ReadLittleEndian32(input, pos);
        // abs = rel + (индекс опкода) + 5; pos = индекс disp = индекс_опкода + 1 → abs = rel + pos + 4.
        uint absolute = unchecked(relative + (uint)pos + 4);

        List<byte> target = b == 0xE8 ? call : jump;
        target.Add((byte)(absolute >> 24));
        target.Add((byte)(absolute >> 16));
        target.Add((byte)(absolute >> 8));
        target.Add((byte)absolute);

        prevByte = (byte)(relative >> 24); // последний байт disp — как в декодере
        pos += 4;
      }
      else
      {
        rc.EncodeBit(ref p[probIndex], 0);
        prevByte = b; // disp остаётся в main и обрабатывается обычными итерациями
      }
    }

    rc.Flush();
    return new SevenZipBcj2Streams(main.ToArray(), call.ToArray(), jump.ToArray(), rc.ToArray());
  }

  // Эвристика конвертации эталонного 7-Zip (Bcj2Enc.c, строки ~288-290).
  private static bool ShouldConvert(byte opcode, int dispPos, uint relative, int fileSize)
  {
    int opcodeIndex = dispPos - 1;

    // Краевое условие: для Jcc нужен индекс > 1, для E8/E9 > 0 (устраняет overlap в начале).
    int edge = ((opcode + 0x20) >> 5) & 1;
    if (opcodeIndex <= edge)
      return false;

    // Абсолютная цель должна попадать в пределы файла (unsigned: отрицательные «уходят» в большие).
    uint absolute = unchecked(relative + (uint)dispPos + 4);
    if (absolute > (uint)(fileSize - 1))
      return false;

    // |relative| < RelativeLimit (трюк из 7-Zip).
    return ((relative + RelativeLimit) >> 1) < RelativeLimit;
  }

  private static uint ReadLittleEndian32(ReadOnlySpan<byte> data, int offset)
      => (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
}
