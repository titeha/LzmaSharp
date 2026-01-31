namespace Lzma.Core.SevenZip;

/// <summary>
/// Кодирование/декодирование UInt64 в формате 7z (вариант ReadNumber из 7-Zip SDK).
/// <para>
/// Важно: это НЕ стандартный LEB128/varint.
/// </para>
/// <para>
/// Схема:
/// - первый байт задаёт количество дополнительных байт (N) количеством ведущих единиц:
///   0xxxxxxx  =&gt; N = 0 (всего 1 байт)
///   10xxxxxx  =&gt; N = 1 (всего 2 байта)
///   110xxxxx  =&gt; N = 2 (всего 3 байта)
///   ...
///   11111110  =&gt; N = 7 (всего 8 байт)
///   11111111  =&gt; N = 8 (всего 9 байт; дальше идут 8 байт значения)
/// - N дополнительных байт содержат младшие 8*N бит числа (little-endian).
/// - оставшаяся старшая часть (до (7-N) бит) хранится в младших битах первого байта.
/// </para>
/// </summary>
internal static class SevenZipEncodedUInt64
{
  internal enum ReadResult
  {
    Ok,
    NeedMoreInput,
  }

  internal enum WriteResult
  {
    Ok,
    NeedMoreOutput,
  }

  /// <summary>
  /// Пытается прочитать UInt64 из <paramref name="input"/> в 7z-представлении.
  /// </summary>
  /// <remarks>
  /// Метод не делает частичных чтений: если данных не хватает, возвращает
  /// <see cref="ReadResult.NeedMoreInput"/> и выставляет <paramref name="bytesRead"/> в 0.
  /// </remarks>
  internal static ReadResult TryRead(ReadOnlySpan<byte> input, out ulong value, out int bytesRead)
  {
    value = 0;
    bytesRead = 0;

    if (input.Length < 1)
      return ReadResult.NeedMoreInput;

    byte first = input[0];

    // Считаем количество ведущих единиц (N).
    int n = 0;
    int mask = 0x80;
    while (n < 8 && (first & mask) != 0)
    {
      n++;
      mask >>= 1;
    }

    int required = (n == 8) ? 9 : (1 + n);
    if (input.Length < required)
      return ReadResult.NeedMoreInput;

    if (n == 8)
    {
      // 0xFF + 8 байт little-endian.
      ulong v = 0;
      for (int i = 0; i < 8; i++)
        v |= (ulong)input[1 + i] << (8 * i);

      value = v;
      bytesRead = 9;
      return ReadResult.Ok;
    }

    // mask сейчас == (0x80 >> n)
    ulong low = 0;
    for (int i = 0; i < n; i++)
      low |= (ulong)input[1 + i] << (8 * i);

    ulong highPart = (ulong)(first & (mask - 1));
    value = low | (highPart << (8 * n));
    bytesRead = required;
    return ReadResult.Ok;
  }

  /// <summary>
  /// Возвращает длину кодирования (в байтах) для <paramref name="value"/> в 7z-представлении.
  /// </summary>
  internal static int GetEncodedLength(ulong value)
  {
    // Для N=0..7 максимальная длина кодируемого числа — 7*(N+1) бит.
    for (int n = 0; n < 8; n++)
    {
      int bits = 7 * (n + 1);
      if (value < (1UL << bits))
        return 1 + n;
    }

    // Остальные значения кодируются как 0xFF + 8 байт.
    return 9;
  }

  /// <summary>
  /// Пытается записать UInt64 в 7z-представлении.
  /// </summary>
  /// <remarks>
  /// Запись также «атомарная»: если <paramref name="destination"/> слишком мал,
  /// возвращает <see cref="WriteResult.NeedMoreOutput"/> и выставляет <paramref name="bytesWritten"/> в 0.
  /// </remarks>
  internal static WriteResult TryWrite(ulong value, Span<byte> destination, out int bytesWritten)
  {
    bytesWritten = 0;

    int len = GetEncodedLength(value);
    if (destination.Length < len)
      return WriteResult.NeedMoreOutput;

    int n = len - 1;
    if (n == 8)
    {
      destination[0] = 0xFF;

      ulong v = value;
      for (int i = 0; i < 8; i++)
      {
        destination[1 + i] = (byte)(v & 0xFF);
        v >>= 8;
      }

      bytesWritten = 9;
      return WriteResult.Ok;
    }

    // Пишем младшие N байт (little-endian).
    ulong lowMask = n == 0 ? 0UL : ((1UL << (8 * n)) - 1UL);
    ulong low = value & lowMask;
    ulong high = value >> (8 * n);

    int mask = 0x80 >> n; // первая «нулевая» позиция
    int highMask = mask - 1;

    int prefixOnes = (0xFF << (8 - n)) & 0xFF; // N ведущих единиц

    destination[0] = (byte)(prefixOnes | (int)(high & (ulong)highMask));

    for (int i = 0; i < n; i++)
      destination[1 + i] = (byte)(low >> (8 * i));

    bytesWritten = len;
    return WriteResult.Ok;
  }
}
