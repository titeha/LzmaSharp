namespace Lzma.Core.Lzma2;

/// <summary>
/// Минимальный LZMA2-энкодер, который НЕ сжимает данные.
/// Он упаковывает вход в последовательность COPY-чанков (uncompressed) и дописывает end marker (0x00).
/// </summary>
/// <remarks>
/// Это намеренно «простая» реализация:
/// <list type="bullet">
/// <item><description>без потокового API;</description></item>
/// <item><description>без настоящего LZMA-сжатия;</description></item>
/// <item><description>зато предсказуемая и удобная как генератор тестовых LZMA2-потоков.</description></item>
/// </list>
///
/// Формат COPY-чанка в LZMA2:
/// <code>
/// control (1 байт):
///   0x01 = COPY + reset dictionary
///   0x02 = COPY + no dictionary reset
/// size (2 байта, big-endian): (chunkSize - 1)
/// payload: chunkSize байт
/// </code>
/// </remarks>
public static class Lzma2CopyEncoder
{
  /// <summary>
  /// Максимальный размер одного COPY-чанка в LZMA2.
  /// Поле размера занимает 2 байта и хранит (size - 1), поэтому максимум = 65536.
  /// </summary>
  public const int MaxChunkSize = 1 << 16; // 65536

  /// <summary>
  /// Кодирует данные в LZMA2-поток, состоящий только из COPY-чанков.
  /// </summary>
  /// <param name="data">Исходные данные.</param>
  /// <param name="resetDictionaryAtStart">
  /// Если true — первый чанк будет с control=0x01 (reset dictionary). Это безопасно для «нового» потока.
  /// Если false — первый чанк будет control=0x02 (без сброса словаря).
  /// </param>
  public static byte[] Encode(ReadOnlySpan<byte> data, bool resetDictionaryAtStart = true)
  {
    // Сколько чанков потребуется (при data.Length == 0 будет 0).
    int chunkCount = (data.Length + MaxChunkSize - 1) / MaxChunkSize;

    // На каждый COPY-чанк уходит 3 байта заголовка, плюс 1 байт end marker.
    int outputSize = data.Length + chunkCount * 3 + 1;

    byte[] encoded = new byte[outputSize];

    int srcPos = 0;
    int dstPos = 0;

    for (int i = 0; i < chunkCount; i++)
    {
      int remaining = data.Length - srcPos;
      int chunkSize = remaining > MaxChunkSize ? MaxChunkSize : remaining;

      encoded[dstPos++] = (i == 0 && resetDictionaryAtStart) ? (byte)0x01 : (byte)0x02;

      // В LZMA2 поле размера — big-endian и хранит (size - 1).
      int sizeMinus1 = chunkSize - 1;
      encoded[dstPos++] = (byte)(sizeMinus1 >> 8);
      encoded[dstPos++] = (byte)sizeMinus1;

      data.Slice(srcPos, chunkSize).CopyTo(encoded.AsSpan(dstPos, chunkSize));
      srcPos += chunkSize;
      dstPos += chunkSize;
    }

    // End marker
    encoded[dstPos++] = 0x00;

    // Защита от ошибки в расчёте размеров.
    if (dstPos != encoded.Length)
      throw new InvalidOperationException("Внутренняя ошибка: рассчитанный размер LZMA2-потока не совпал с фактическим.");

    return encoded;
  }
}
