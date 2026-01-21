namespace Lzma.Core.Lzma2;

/// <summary>
/// Свойства LZMA2-кодера (в 7z это 1 байт, задающий размер словаря).
/// </summary>
/// <remarks>
/// <para>
/// Важно не путать с LZMA properties (lc/lp/pb): они относятся к самой LZMA-модели
/// и встречаются <b>внутри</b> LZMA2-потока (в LZMA-чанках, когда выставлен флаг propsIncluded).
/// </para>
/// <para>
/// В LZMA2 этот байт кодирует размер словаря по формуле:
/// <list type="bullet">
/// <item><description>prop ∈ [0..40]</description></item>
/// <item><description>если prop == 40, то dictSize = 0xFFFF_FFFF</description></item>
/// <item><description>иначе dictSize = (2 | (prop &amp; 1)) &lt;&lt; (prop / 2 + 11)</description></item>
/// </list>
/// </para>
/// </remarks>
public readonly record struct Lzma2Properties(byte DictionaryProp, uint DictionarySize)
{
  /// <summary>
  /// Максимальное допустимое значение property byte для LZMA2 (по формату).
  /// </summary>
  public const byte MaxDictionaryProp = 40;

  /// <summary>
  /// Пытается разобрать LZMA2 properties byte и вычислить размер словаря.
  /// </summary>
  /// <param name="dictionaryProp">Байт properties (0..40).</param>
  /// <param name="properties">Результирующие свойства.</param>
  /// <returns><c>true</c>, если <paramref name="dictionaryProp"/> в допустимом диапазоне.</returns>
  public static bool TryParse(byte dictionaryProp, out Lzma2Properties properties)
  {
    if (dictionaryProp > MaxDictionaryProp)
    {
      properties = default;
      return false;
    }

    uint dictSize;

    if (dictionaryProp == MaxDictionaryProp)
    {
      // Специальное значение из LZMA SDK: "максимально возможный размер".
      // На практике такое значение почти всегда означает "больше, чем мы реально можем выделить".
      dictSize = 0xFFFF_FFFFu;
    }
    else
    {
      uint baseValue = (uint)(2 | (dictionaryProp & 1));
      int shift = (dictionaryProp / 2) + 11;
      dictSize = baseValue << shift;
    }

    properties = new Lzma2Properties(dictionaryProp, dictSize);
    return true;
  }

  /// <summary>
  /// Пытается получить размер словаря как <see cref="int"/>.
  /// </summary>
  /// <remarks>
  /// Это удобно, потому что текущая версия <see cref="Lzma.Core.Lzma1.LzmaDictionary"/>
  /// использует <see cref="int"/> для размера.
  /// </remarks>
  public bool TryGetDictionarySizeInt32(out int dictionarySize)
  {
    if (DictionarySize == 0 || DictionarySize > (uint)int.MaxValue)
    {
      dictionarySize = 0;
      return false;
    }

    dictionarySize = (int)DictionarySize;
    return true;
  }
}
