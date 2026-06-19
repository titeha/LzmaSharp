namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>
/// Ценовая модель LZMA: оценивает «стоимость» кодирования бита/символа в фиксированной
/// точке (цена бита = <c>1 &lt;&lt; 4</c> = 16 единиц ≈ −log2 вероятности). Нужна для
/// optimal parsing: чтобы выбирать дешевейшую последовательность операций (литерал / match /
/// rep) по реальной стоимости в битах согласно текущим вероятностям модели.
/// </para>
/// <para>
/// Точный порт ценовой подсистемы эталона LZMA SDK (LzmaEnc.c: <c>LzmaEnc_InitPriceTables</c>,
/// <c>GET_PRICE*</c>, <c>BitTreeEncode/ReverseEncode</c> цены). Цены зеркалят кодирование
/// в <see cref="LzmaRangeEncoder"/> и <see cref="LzmaBitTreeEncoder"/>.
/// </para>
/// </summary>
internal static class LzmaPrice
{
  /// <summary>На сколько бит «сжимается» индекс вероятности при доступе к таблице цен.</summary>
  private const int NumMoveReducingBits = 4;

  /// <summary>Сдвиг фиксированной точки цены: цена одного бита = <c>1 &lt;&lt; 4</c> = 16.</summary>
  private const int NumBitPriceShiftBits = 4;

  /// <summary>Цена ровно одного «идеального» бита (вероятность 1/2) в единицах фиксированной точки.</summary>
  public const int BitPrice = 1 << NumBitPriceShiftBits;

  // Таблица цен: ProbPrices[prob >> NumMoveReducingBits] = −log2(prob/Total) в fixed-point.
  private static readonly uint[] ProbPrices = BuildProbPrices();

  private static uint[] BuildProbPrices()
  {
    int size = LzmaConstants.BitModelTotal >> NumMoveReducingBits;
    var table = new uint[size];

    for (uint i = 0; i < size; i++)
    {
      const int cyclesBits = NumBitPriceShiftBits;
      uint w = (i << NumMoveReducingBits) + (1u << (NumMoveReducingBits - 1));
      uint bitCount = 0;

      for (int j = 0; j < cyclesBits; j++)
      {
        w *= w;
        bitCount <<= 1;
        while (w >= (1u << 16))
        {
          w >>= 1;
          bitCount++;
        }
      }

      table[i] = (uint)((LzmaConstants.NumBitModelTotalBits << cyclesBits) - 15 - bitCount);
    }

    return table;
  }

  /// <summary>Цена кодирования бита 0 при вероятности <paramref name="prob"/>.</summary>
  public static uint Price0(ushort prob) => ProbPrices[prob >> NumMoveReducingBits];

  /// <summary>Цена кодирования бита 1 при вероятности <paramref name="prob"/>.</summary>
  public static uint Price1(ushort prob)
      => ProbPrices[(prob ^ (LzmaConstants.BitModelTotal - 1)) >> NumMoveReducingBits];

  /// <summary>Цена кодирования бита <paramref name="bit"/> при вероятности <paramref name="prob"/>.</summary>
  public static uint Price(ushort prob, uint bit) => bit == 0 ? Price0(prob) : Price1(prob);

  /// <summary>
  /// Цена «обычного» (MSB-first) обхода bit-tree для символа — зеркало
  /// <see cref="LzmaBitTreeEncoder.EncodeSymbol"/>.
  /// </summary>
  public static uint BitTreePrice(ReadOnlySpan<ushort> probs, int numBits, uint symbol)
  {
    uint price = 0;
    int index = 1;

    for (int bitIndex = numBits; bitIndex != 0; bitIndex--)
    {
      uint bit = (symbol >> (bitIndex - 1)) & 1u;
      price += Price(probs[index], bit);
      index = (index << 1) + (int)bit;
    }

    return price;
  }

  /// <summary>
  /// Цена «обратного» (LSB-first) обхода bit-tree для символа — зеркало
  /// <see cref="LzmaBitTreeEncoder.EncodeReverseSymbol"/>.
  /// </summary>
  public static uint BitTreeReversePrice(ReadOnlySpan<ushort> probs, int numBits, uint symbol)
  {
    uint price = 0;
    int index = 1;

    for (int i = 0; i < numBits; i++)
    {
      uint bit = symbol & 1u;
      price += Price(probs[index], bit);
      index = (index << 1) + (int)bit;
      symbol >>= 1;
    }

    return price;
  }
}
