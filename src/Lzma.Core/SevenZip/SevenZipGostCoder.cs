namespace Lzma.Core.SevenZip;

/// <summary>
/// Экспериментальные private method id для GOST-веток LzmaSharp.
/// </summary>
/// <remarks>
/// Это не стандартные method id 7-Zip.
/// Они используются только как внутреннее расширение формата для LzmaSharp.
/// </remarks>
public static class SevenZipGostCoder
{
  // Private experimental IDs по схеме 3F ... MM MM.
  // Префикс фиксируем внутри проекта и не меняем без крайней необходимости.

  /// <summary>
  /// Experimental method id для шифрования Кузнечик.
  /// </summary>
  public static ReadOnlySpan<byte> KuznyechikMethodId => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x01];

  /// <summary>
  /// Experimental method id для шифрования Магма.
  /// </summary>
  public static ReadOnlySpan<byte> MagmaMethodId => [0x3F, 0xD1, 0x6A, 0x52, 0x8C, 0x01, 0x00, 0x02];

  /// <summary>
  /// Проверяет, относится ли method id к экспериментальным GOST coder-ам LzmaSharp.
  /// </summary>
  public static bool IsGostMethodId(ReadOnlySpan<byte> methodId) => IsKuznyechikMethodId(methodId) || IsMagmaMethodId(methodId);

  /// <summary>
  /// Проверяет, является ли method id coder-ом Кузнечик.
  /// </summary>
  public static bool IsKuznyechikMethodId(ReadOnlySpan<byte> methodId) => methodId.SequenceEqual(KuznyechikMethodId);

  /// <summary>
  /// Проверяет, является ли method id coder-ом Магма.
  /// </summary>
  public static bool IsMagmaMethodId(ReadOnlySpan<byte> methodId) => methodId.SequenceEqual(MagmaMethodId);
}
