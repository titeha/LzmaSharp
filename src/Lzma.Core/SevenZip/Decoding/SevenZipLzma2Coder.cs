namespace Lzma.Core.SevenZip;

/// <summary>
/// Константы и вспомогательные методы для кодера LZMA2 в формате 7z.
/// </summary>
public static class SevenZipLzma2Coder
{
  /// <summary>
  /// Method ID кодера LZMA2 в 7z.
  /// </summary>
  public const byte MethodIdByte = 0x21;

  /// <summary>
  /// Создаёт описание кодера LZMA2 для <see cref="SevenZipUnpackInfo"/>:
  /// 1 входной поток → 1 выходной поток.
  /// </summary>
  public static SevenZipCoderInfo Create(byte propertiesByte) => new(
      methodId: [MethodIdByte],
      properties: [propertiesByte],
      numInStreams: 1,
      numOutStreams: 1);

  /// <summary>
  /// Пытается декодировать размер словаря (dictionary size) из 1 байта properties LZMA2 (формат 7z).
  /// </summary>
  /// <remarks>
  /// p ∈ [0..40]. p==40 означает 0xFFFFFFFF (4 ГБ - 1).
  /// Формула соответствует описанию из LZMA SDK / lzma-specification.
  /// </remarks>
  public static bool TryDecodeDictionarySize(byte properties, out uint dictionarySize)
  {
    if (properties > 40)
    {
      dictionarySize = 0;
      return false;
    }

    if (properties == 40)
    {
      dictionarySize = 0xFFFFFFFF;
      return true;
    }

    dictionarySize = (2 | (properties & 1u)) << (properties / 2 + 11);
    return true;
  }
}
