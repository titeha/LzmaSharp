namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>Декодер литералов (одиночных байт) для LZMA.</para>
/// <para>
/// В LZMA каждый байт кодируется 8 битами через range coder (арифметический декодер)
/// и дерево вероятностей.
/// </para>
/// <para>
/// Главная особенность LZMA: вероятности выбираются по контексту.
/// Контекст определяется параметрами lc/lp и зависит от:
/// - позиции (lp младших бит позиции),
/// - предыдущего байта (lc старших бит предыдущего байта).
/// </para>
/// <para>
/// Количество контекстов: 1 &lt;&lt; (lc + lp).
/// На каждый контекст выделяется массив вероятностей из 0x300 (768) элементов.
/// </para>
/// <para>
/// Важно:
/// - Это «чистая» реализация без оптимизаций и трюков с памятью.
/// - Метод TryDecode* не является атомарным: если в середине декодирования не хватит входных байт,
///   часть вероятностей может успеть измениться. Для стриминга мы позже добавим слой,
///   который обеспечивает атомарность на уровне «декодируем один символ целиком или не декодируем».
/// </para>
/// </summary>
public sealed class LzmaLiteralDecoder
{
  private readonly int _lc;
  private readonly int _lp;
  private readonly int _posMask;
  private readonly SubDecoder[] _subDecoders;

  /// <summary>
  /// Создаёт декодер литералов.
  /// </summary>
  /// <param name="lc">Количество бит контекста из предыдущего байта (0..8).</param>
  /// <param name="lp">Количество бит контекста из позиции (0..4).</param>
  public LzmaLiteralDecoder(int lc, int lp)
  {
    if ((uint)lc > 8)
      throw new ArgumentOutOfRangeException(nameof(lc), "lc должен быть в диапазоне 0..8.");
    if ((uint)lp > 4)
      throw new ArgumentOutOfRangeException(nameof(lp), "lp должен быть в диапазоне 0..4.");

    _lc = lc;
    _lp = lp;

    // Если lp = 0, маска станет 0 (то есть posPart всегда 0).
    _posMask = (1 << lp) - 1;

    int numContexts = 1 << (lc + lp);
    _subDecoders = new SubDecoder[numContexts];
    for (int i = 0; i < _subDecoders.Length; i++)
      _subDecoders[i] = new SubDecoder();

    Reset();
  }

  public int Lc => _lc;
  public int Lp => _lp;

  /// <summary>
  /// Количество контекстов.
  /// </summary>
  public int ContextCount => _subDecoders.Length;

  /// <summary>
  /// Вычисляет индекс контекста для декодирования литерала.
  /// Формула совпадает с SDK 7-Zip.
  /// </summary>
  public int ComputeContextIndex(int position, byte previousByte)
  {
    int posPart = position & _posMask;

    // Старшие lc бит предыдущего байта.
    // Если lc == 0, prevPart должен быть 0.
    int prevPart = _lc == 0 ? 0 : (previousByte >> (8 - _lc));

    return (posPart << _lc) + prevPart;
  }

  /// <summary>
  /// Сбрасывает все вероятности во всех контекстах.
  /// </summary>
  public void Reset()
  {
    foreach (var d in _subDecoders)
      d.Reset();
  }

  /// <summary>
  /// Возвращает конкретную вероятность для тестов/отладки.
  /// </summary>
  public ushort GetProbability(int contextIndex, int probabilityIndex)
  {
    if ((uint)contextIndex >= (uint)_subDecoders.Length)
      throw new ArgumentOutOfRangeException(nameof(contextIndex));

    return _subDecoders[contextIndex].GetProbability(probabilityIndex);
  }

  /// <summary>
  /// Декодирует литерал в «обычном» режиме (без matchByte).
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeNormal(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      int position,
      byte previousByte,
      out byte literal)
  {
    int ctx = ComputeContextIndex(position, previousByte);
    return _subDecoders[ctx].TryDecodeNormal(ref range, input, ref inputOffset, out literal);
  }

  /// <summary>
  /// Декодирует литерал в режиме «с matchByte».
  ///
  /// Этот режим используется, когда текущее состояние LZMA говорит,
  /// что после match/rep вероятность следующего литерала зависит от байта,
  /// который находится в словаре по текущей match-дистанции.
  /// </summary>
  public LzmaRangeDecodeResult TryDecodeWithMatchByte(
      ref LzmaRangeDecoder range,
      ReadOnlySpan<byte> input,
      ref int inputOffset,
      int position,
      byte previousByte,
      byte matchByte,
      out byte literal)
  {
    int ctx = ComputeContextIndex(position, previousByte);
    return _subDecoders[ctx].TryDecodeWithMatchByte(ref range, input, ref inputOffset, matchByte, out literal);
  }

  private sealed class SubDecoder
  {
    // 0x300 = 768. Индексы 0..0x2FF.
    // Ноды дерева для литерала используют диапазон 1..0x1FF, а режим matchByte использует и 0x200..0x2FF.
    private const int ProbabilityCount = 0x300;

    private readonly ushort[] _probs = new ushort[ProbabilityCount];

    public void Reset() => LzmaProbability.Reset(_probs);

    public ushort GetProbability(int probabilityIndex)
    {
      if ((uint)probabilityIndex >= (uint)_probs.Length)
        throw new ArgumentOutOfRangeException(nameof(probabilityIndex));

      return _probs[probabilityIndex];
    }

    public LzmaRangeDecodeResult TryDecodeNormal(
        ref LzmaRangeDecoder range,
        ReadOnlySpan<byte> input,
        ref int inputOffset,
        out byte literal)
    {
      // В SDK это делается битовым деревом:
      // symbol = 1;
      // while(symbol < 0x100) symbol = (symbol<<1) | DecodeBit(probs[symbol]);
      uint symbol = 1;

      while (symbol < 0x100)
      {
        var res = range.TryDecodeBit(ref _probs[(int)symbol], input, ref inputOffset, out uint bit);
        if (res != LzmaRangeDecodeResult.Ok)
        {
          literal = 0;
          return res;
        }

        symbol = (symbol << 1) | bit;
      }

      literal = (byte)symbol;
      return LzmaRangeDecodeResult.Ok;
    }

    public LzmaRangeDecodeResult TryDecodeWithMatchByte(
        ref LzmaRangeDecoder range,
        ReadOnlySpan<byte> input,
        ref int inputOffset,
        byte matchByte,
        out byte literal)
    {
      uint symbol = 1;

      while (symbol < 0x100)
      {
        uint matchBit = (uint)((matchByte >> 7) & 1);
        matchByte <<= 1;

        // Индекс вероятности выбирается из «верхней половины» массива:
        // ((1 + matchBit) << 8) + symbol
        int probIndex = (int)(((1 + matchBit) << 8) + symbol);

        var res = range.TryDecodeBit(ref _probs[probIndex], input, ref inputOffset, out uint bit);
        if (res != LzmaRangeDecodeResult.Ok)
        {
          literal = 0;
          return res;
        }

        symbol = (symbol << 1) | bit;

        if (bit != matchBit)
        {
          // Если мы «разошлись» с matchByte, оставшиеся биты декодируем обычным деревом.
          while (symbol < 0x100)
          {
            res = range.TryDecodeBit(ref _probs[(int)symbol], input, ref inputOffset, out bit);
            if (res != LzmaRangeDecodeResult.Ok)
            {
              literal = 0;
              return res;
            }

            symbol = (symbol << 1) | bit;
          }

          break;
        }
      }

      literal = (byte)symbol;
      return LzmaRangeDecodeResult.Ok;
    }
  }
}
