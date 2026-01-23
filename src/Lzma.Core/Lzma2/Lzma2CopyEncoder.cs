namespace Lzma.Core.Lzma2;

/// <summary>
/// Минимальный LZMA2-энкодер, который НЕ сжимает данные.
/// Он пишет поток только из COPY-chunks и завершает его маркером 0x00.
/// </summary>
public static class Lzma2CopyEncoder
{
  public const int MaxChunkSize = 1 << 16;

  /// <summary>
  /// Размер словаря по умолчанию (нужен только для вычисления LZMA2 properties byte,
  /// который используется в контейнерах вроде 7z).
  /// </summary>
  public const int DefaultDictionarySize = 1 << 23;

  /// <summary>
  /// Кодирует данные в LZMA2-поток (только COPY-chunks).
  /// </summary>
  /// <param name="data">Исходные данные.</param>
  /// <param name="resetDictionaryAtStart">
  /// Если true — первый chunk будет с reset dict (control=0x01), иначе — без reset (control=0x02).
  /// </param>
  public static byte[] Encode(ReadOnlySpan<byte> data, bool resetDictionaryAtStart = true)
  {
    // COPY chunk: control (1) + sizeMinus1 (2) + payload (N)
    // End marker: 0x00 (1)
    int chunkCount = (data.Length + MaxChunkSize - 1) / MaxChunkSize;
    int outLen = chunkCount * 3 + data.Length + 1;

    var output = new byte[outLen];

    int inPos = 0;
    int outPos = 0;

    // Если resetDictionaryAtStart == false, то даже первый chunk будет 0x02.
    byte control = resetDictionaryAtStart ? (byte)0x01 : (byte)0x02;

    while (inPos < data.Length)
    {
      int chunkSize = Math.Min(MaxChunkSize, data.Length - inPos);
      int sizeMinus1 = chunkSize - 1;

      output[outPos++] = control;
      output[outPos++] = (byte)(sizeMinus1 >> 8);
      output[outPos++] = (byte)(sizeMinus1);

      data.Slice(inPos, chunkSize).CopyTo(output.AsSpan(outPos));
      inPos += chunkSize;
      outPos += chunkSize;

      // После первого чанка reset dict уже не бывает (если только не начинается новый stream).
      control = 0x02;
    }

    // End marker
    output[outPos++] = 0x00;

    return output;
  }

  /// <summary>
  /// То же самое, но дополнительно возвращает LZMA2 properties byte (кодированный размер словаря).
  /// Это значение не влияет на COPY-распаковку, но потребуется для контейнеров (например, 7z).
  /// </summary>
  public static byte[] Encode(ReadOnlySpan<byte> data, out byte propertiesByte, bool resetDictionaryAtStart = true) =>
    Encode(data, DefaultDictionarySize, out propertiesByte, resetDictionaryAtStart);

  /// <summary>
  /// Кодирует данные и возвращает properties byte, соответствующий <paramref name="dictionarySize"/>.
  /// </summary>
  public static byte[] Encode(ReadOnlySpan<byte> data, int dictionarySize, out byte propertiesByte, bool resetDictionaryAtStart = true)
  {
    if (!Lzma2Properties.TryEncode(dictionarySize, out propertiesByte))
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Некорректный размер словаря для LZMA2.");

    return Encode(data, resetDictionaryAtStart);
  }
}
