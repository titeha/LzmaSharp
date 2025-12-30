namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Общие константы и вспомогательные методы для вероятностей (prob) в LZMA.</para>
/// <para>
/// В RangeCoder LZMA использует фиксированную шкалу вероятностей:
///   kNumBitModelTotalBits = 11
///   kBitModelTotal        = 1 << 11 = 2048
///
/// Начальное значение вероятности для каждого узла дерева/модели:
///   kBitModelTotal / 2 = 1024
///
/// Мы держим вероятности в ushort'ах и обновляем их при декодировании битов.
/// </para>
/// </summary>
public static class LzmaProbability
{
  /// <summary>
  /// Начальная вероятность (kBitModelTotal / 2).
  /// </summary>
  public const ushort Initial = 1024;

  /// <summary>
  /// Заполняет массив вероятностей начальным значением.
  /// </summary>
  public static void Reset(Span<ushort> probs) => probs.Fill(Initial);

  /// <summary>
  /// Заполняет массив вероятностей начальным значением.
  /// </summary>
  public static void Reset(ushort[] probs) => probs.AsSpan().Fill(Initial);
}
