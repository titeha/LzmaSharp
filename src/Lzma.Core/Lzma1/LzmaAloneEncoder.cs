namespace Lzma.Core.Lzma1;

/// <summary>
/// Утилита для кодирования потока в формате "LZMA Alone".
/// </summary>
/// <remarks>
/// На данном шаге это в основном тестовый/вспомогательный энкодер:
/// - умеет собрать заголовок (properties + dictionarySize + uncompressedSize);
/// - умеет закодировать payload нашим <see cref="LzmaEncoder"/> и склеить с заголовком.
/// </remarks>
public static class LzmaAloneEncoder
{
  /// <summary>
  /// Кодирует входной буфер в формат LZMA-Alone, используя ТОЛЬКО литералы.
  /// </summary>
  /// <remarks>
  /// Это намеренное упрощение для тестов: на данном шаге энкодер не ищет match'и.
  /// </remarks>
  public static byte[] EncodeLiteralOnly(ReadOnlySpan<byte> input, LzmaProperties properties, int dictionarySize)
  {
    var header = new LzmaAloneHeader(properties, dictionarySize, (ulong)input.Length);

    // Кодируем payload нашим LZMA-энкодером.
    var encoder = new LzmaEncoder(properties, dictionarySize);
    byte[] payload = encoder.EncodeLiteralOnly(input);

    var output = new byte[LzmaAloneHeader.HeaderSize + payload.Length];

    if (!header.TryWrite(output.AsSpan(0, LzmaAloneHeader.HeaderSize), out int headerBytesWritten) ||
        headerBytesWritten != LzmaAloneHeader.HeaderSize)
      throw new InvalidOperationException("Не удалось сформировать LZMA-Alone заголовок.");

    payload.CopyTo(output, headerBytesWritten);
    return output;
  }

  /// <summary>
  /// Кодирует заданный "скрипт" (литералы + match'и) в поток LZMA-Alone.
  /// </summary>
  /// <remarks>
  /// Это вспомогательный метод для тестов: мы заранее знаем последовательность операций,
  /// поэтому можем проверять декодер и отдельные блоки энкодера маленькими шагами.
  /// </remarks>
  internal static byte[] EncodeScript(
    ReadOnlySpan<LzmaEncodeOp> script,
    LzmaProperties properties,
    int dictionarySize)
  {
    // Считаем ожидаемый размер распакованных данных (нужен для заголовка LZMA-Alone).
    ulong uncompressedSize = 0;
    foreach (var op in script)
    {
      if (op.Kind == LzmaEncodeOpKind.Literal)
      {
        uncompressedSize++;
        continue;
      }

      if (op.Kind == LzmaEncodeOpKind.Match)
      {
        if (op.Length <= 0)
          throw new ArgumentOutOfRangeException(nameof(script), "Длина match должна быть > 0.");

        uncompressedSize += (ulong)op.Length;
        continue;
      }

      throw new InvalidOperationException("Неизвестный тип операции кодирования.");
    }

    var header = new LzmaAloneHeader(properties, dictionarySize, uncompressedSize);

    // Кодируем payload нашим LZMA-энкодером.
    var encoder = new LzmaEncoder(properties, dictionarySize);
    byte[] payload = encoder.EncodeScript(script);

    var output = new byte[LzmaAloneHeader.HeaderSize + payload.Length];

    if (!header.TryWrite(output.AsSpan(0, LzmaAloneHeader.HeaderSize), out int headerBytesWritten) ||
        headerBytesWritten != LzmaAloneHeader.HeaderSize)
      throw new InvalidOperationException("Не удалось сформировать LZMA-Alone заголовок.");

    payload.CopyTo(output, headerBytesWritten);
    return output;
  }
}
