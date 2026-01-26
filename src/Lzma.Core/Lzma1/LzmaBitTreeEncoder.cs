namespace Lzma.Core.Lzma1;

/// <summary>
/// Битовое дерево (BitTree) для КОДИРОВАНИЯ значений с помощью range encoder'а.
/// </summary>
/// <remarks>
/// <para>Это парный класс к <see cref="LzmaBitTreeDecoder"/>.</para>
/// <para>
/// В LZMA многие числа кодируются не «прямо бинарником», а проходом по бинарному дереву,
/// где каждый шаг — это один бит, закодированный через адаптивную вероятность.
/// </para>
/// <para>
/// На этом шаге нам нужен только понятный и предсказуемый building-block, чтобы дальше
/// собрать минимальный LZMA-энкодер (сначала литералы, потом матчи).
/// </para>
/// </remarks>
internal sealed class LzmaBitTreeEncoder
{
  /// <summary>
  /// Вероятности узлов дерева.
  /// </summary>
  /// <remarks>
  /// Длина массива: 2^numBits.
  /// Индексация совпадает с <see cref="LzmaBitTreeDecoder"/>.
  /// </remarks>
  public ushort[] Probs { get; }

  /// <summary>
  /// Количество бит (глубина дерева).
  /// </summary>
  public int NumBits { get; }

  public LzmaBitTreeEncoder(int numBits)
  {
    // В LZMA числа обычно небольшие. Ограничение в 30 бит — чтобы не получить переполнение при (1 << numBits).
    if (numBits <= 0 || numBits > 30)
      throw new ArgumentOutOfRangeException(nameof(numBits), "numBits должен быть в диапазоне 1..30.");

    NumBits = numBits;
    Probs = new ushort[1 << numBits];
    Reset();
  }

  /// <summary>
  /// Сбрасывает вероятности дерева в начальное состояние.
  /// </summary>
  public void Reset() => LzmaProbability.Reset(Probs);

  /// <summary>
  /// Кодирует символ «обычным» (MSB-first) обходом дерева.
  /// </summary>
  public void EncodeSymbol(LzmaRangeEncoder range, uint symbol)
  {
    uint limit = 1u << NumBits;
    if (symbol >= limit)
      throw new ArgumentOutOfRangeException(nameof(symbol), $"symbol должен быть < {limit} для numBits={NumBits}.");

    int index = 1;

    // Идём от старшего бита к младшему (как в TryDecodeSymbol).
    for (int bitIndex = NumBits; bitIndex != 0; bitIndex--)
    {
      uint bit = (symbol >> (bitIndex - 1)) & 1u;
      range.EncodeBit(ref Probs[index], bit);
      index = (index << 1) + (int)bit;
    }
  }

  /// <summary>
  /// Синоним для <see cref="EncodeReverseSymbol"/>.
  /// </summary>
  /// <remarks>
  /// В коде энкодера расстояний естественнее видеть <c>EncodeReverse</c>,
  /// но реализация исторически называлась <c>EncodeReverseSymbol</c>.
  /// Поддерживаем оба имени, чтобы не разъезжались сигнатуры между шагами.
  /// </remarks>
  public void EncodeReverse(LzmaRangeEncoder range, uint symbol) => EncodeReverseSymbol(range, symbol);


  /// <summary>
  /// Кодирует символ «обратным» (LSB-first) обходом дерева.
  /// </summary>
  /// <remarks>
  /// Это понадобится для кодирования некоторых частей distance (в частности, align/low bits),
  /// где LZMA использует обратный порядок бит.
  /// </remarks>
  public void EncodeReverseSymbol(LzmaRangeEncoder range, uint symbol)
  {
    uint limit = 1u << NumBits;
    if (symbol >= limit)
      throw new ArgumentOutOfRangeException(nameof(symbol), $"symbol должен быть < {limit} для numBits={NumBits}.");

    int index = 1;

    for (int i = 0; i < NumBits; i++)
    {
      uint bit = symbol & 1u;
      range.EncodeBit(ref Probs[index], bit);
      index = (index << 1) + (int)bit;
      symbol >>= 1;
    }
  }
}
