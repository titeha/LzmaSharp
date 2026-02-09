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
}
