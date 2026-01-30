namespace Lzma.Core.Lzma1;

/// <summary>
/// Минимальный энкодер формата «LZMA-Alone».
/// </summary>
/// <remarks>
/// <para>
/// Формат LZMA-Alone = 13-байтный заголовок + LZMA1-поток.
/// Заголовок (13 байт):
///  - 1 байт  : properties (lc/lp/pb)
///  - 4 байта : dictionary size (LE)
///  - 8 байт  : uncompressed size (LE), либо 0xFFFF_FFFF_FFFF_FFFF если размер неизвестен.
/// </para>
/// <para>
/// На текущем шаге нам достаточно "literal-only" режима: это нужно в тестах, чтобы
/// можно было «собрать» небольшой LZMA-Alone поток и затем проверить декодер.
/// </para>
/// </remarks>
public static class LzmaAloneEncoder
{
  /// <summary>
  /// Кодирует данные в LZMA-Alone, используя упрощённый LZMA1-энкодер,
  /// который генерирует ТОЛЬКО литералы (без match'ей).
  /// </summary>
  /// <param name="input">Исходные данные.</param>
  /// <param name="properties">LZMA properties (lc/lp/pb).</param>
  /// <param name="dictionarySize">Размер словаря (должен быть &gt; 0).</param>
  /// <returns>Полный LZMA-Alone поток: header + payload.</returns>
  public static byte[] EncodeLiteralOnly(ReadOnlySpan<byte> input, LzmaProperties properties, int dictionarySize)
  {
    if (dictionarySize <= 0)
      throw new ArgumentOutOfRangeException(nameof(dictionarySize), "Размер словаря должен быть > 0.");

    // Проверяем, что properties действительно кодируются в 1 байт.
    // (В заголовке LZMA-Alone properties хранятся именно так.)
    if (!properties.TryToByte(out _))
      throw new ArgumentOutOfRangeException(nameof(properties), "Некорректные LZMA properties.");

    // Для LZMA-Alone мы пишем известный размер распакованных данных.
    // (В LZMA-Alone допускается и "неизвестный" размер, но на этом шаге он не нужен.)
    // Важно: LzmaAloneHeader хранит именно LzmaProperties (а не сырой байт).
    var header = new LzmaAloneHeader(properties, dictionarySize, (ulong)input.Length);

    // Тело: LZMA1-поток (range-coded), в упрощённом варианте без match'ей.
    var encoder = new LzmaEncoder(properties, dictionarySize);
    byte[] payload = encoder.EncodeLiteralOnly(input);

    // Собираем итоговый поток: header + payload.
    var result = new byte[LzmaAloneHeader.HeaderSize + payload.Length];

    bool ok = header.TryWrite(result, out int headerWritten);
    if (!ok || headerWritten != LzmaAloneHeader.HeaderSize)
      throw new InvalidOperationException("Не удалось записать LZMA-Alone header.");

    payload.AsSpan().CopyTo(result.AsSpan(LzmaAloneHeader.HeaderSize));
    return result;
  }
}
