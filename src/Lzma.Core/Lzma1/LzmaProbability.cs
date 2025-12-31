namespace Lzma.Core.Lzma1;

/// <summary>
/// В LZMA большинство битов декодируются через range-кодер с «адаптивной вероятностью».
/// 
/// В оригинальном SDK это тип <c>CLzmaProb</c>, обычно <c>UInt16</c>.
/// Здесь мы оставляем простую и понятную модель: вероятность = <c>ushort</c>.
/// </summary>
public static class LzmaProbability
{
  /// <summary>
  /// Начальное значение вероятности (1/2 шкалы).
  /// </summary>
  public const ushort Initial = LzmaConstants.BitModelTotal / 2;

  /// <summary>
  /// Сбросить вероятности в «середину» шкалы.
  /// </summary>
  public static void Reset(ushort[] probs)
  {
    if (probs is null)
      throw new ArgumentNullException(nameof(probs));

    Reset(probs.AsSpan());
  }

  /// <summary>
  /// Сбросить вероятности в «середину» шкалы.
  /// </summary>
  public static void Reset(Span<ushort> probs)
  {
    probs.Fill(Initial);
  }
}
