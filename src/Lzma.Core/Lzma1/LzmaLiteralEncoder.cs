namespace Lzma.Core.Lzma1;

/// <summary>
/// Кодер литералов LZMA.
/// </summary>
/// <remarks>
/// <para>
/// Этот класс отвечает <b>только</b> за кодирование дерева литерала.
/// Решения «литерал/матч» (<c>isMatch</c>) и вычисление <c>matchByte</c>
/// делаются снаружи (в более высоком уровне кодера).
/// </para>
/// <para>
/// Контекст выбирается по параметрам (<c>lc</c>/<c>lp</c>):
/// </para>
/// <list type="bullet">
/// <item><description><c>lp</c> — младшие биты позиции (сколько уже выведено байт)</description></item>
/// <item><description><c>lc</c> — старшие биты предыдущего байта</description></item>
/// </list>
/// </remarks>
internal sealed class LzmaLiteralEncoder
{
  private const int _literalCoderSize = 0x300; // 3 * 0x100

  private readonly int _lc;
  private readonly int _lp;
  private readonly int _lpMask;

  /// <summary>
  /// Количество контекстов ( = 1 &lt;&lt; (lc + lp)).
  /// </summary>
  public int ContextCount { get; }

  /// <summary>
  /// Массив вероятностей (по сути — «память» модели).
  /// </summary>
  public ushort[] Probs { get; }

  /// <summary>
  /// Создаёт кодер литералов для заданных <c>lc</c>/<c>lp</c>.
  /// </summary>
  public LzmaLiteralEncoder(int lc, int lp)
  {
    if ((uint)lc > 8)
      throw new ArgumentOutOfRangeException(nameof(lc), "lc должен быть в диапазоне [0..8].");

    if ((uint)lp > 4)
      throw new ArgumentOutOfRangeException(nameof(lp), "lp должен быть в диапазоне [0..4].");

    if (lc + lp > 8)
      throw new ArgumentOutOfRangeException(nameof(lc), "Ограничение LZMA: lc + lp <= 8.");

    _lc = lc;
    _lp = lp;
    _lpMask = (1 << lp) - 1;

    ContextCount = 1 << (lc + lp);
    Probs = new ushort[ContextCount * _literalCoderSize];

    Reset();
  }

  /// <summary>
  /// Сбрасывает вероятности к исходным значениям.
  /// </summary>
  public void Reset()
  {
    LzmaProbability.Reset(Probs);
  }

  /// <summary>
  /// Вычисляет индекс контекста (0..ContextCount-1) по позиции и предыдущему байту.
  /// </summary>
  internal int ComputeContextIndex(long position, byte previousByte)
  {
    int low = (int)position & _lpMask;
    int high = previousByte >> (8 - _lc);
    return (low << _lc) + high;
  }

  /// <summary>
  /// Возвращает смещение под-кодера (базовый индекс) для дерева литерала.
  /// </summary>
  internal int GetSubCoderOffset(long position, byte previousByte)
  {
    return ComputeContextIndex(position, previousByte) * _literalCoderSize;
  }

  /// <summary>
  /// Кодирует литерал, когда <c>isMatch == 0</c> (обычный литерал).
  /// </summary>
  public void EncodeNormal(ref LzmaRangeEncoder range, long position, byte previousByte, byte literal)
  {
    int baseIndex = GetSubCoderOffset(position, previousByte);

    // 8-битное дерево: начинаем с 1, на каждом шаге добавляем бит.
    int symbol = 1;
    for (int i = 7; i >= 0; i--)
    {
      uint bit = (uint)((literal >> i) & 1);
      range.EncodeBit(ref Probs[baseIndex + symbol], bit);
      symbol = (symbol << 1) | (int)bit;
    }
  }

  /// <summary>
  /// Кодирует литерал в режиме «matched literal».
  /// </summary>
  /// <remarks>
  /// <para>
  /// Этот режим используется внутри match/rep: декодер сравнивает биты с <paramref name="matchByte"/>
  /// и до первого расхождения читает биты из «matched»-поддерева.
  /// После расхождения оставшиеся биты кодируются обычным деревом.
  /// </para>
  /// </remarks>
  public void EncodeMatched(
    ref LzmaRangeEncoder range,
    long position,
    byte previousByte,
    byte matchByte,
    byte literal)
  {
    int baseIndex = GetSubCoderOffset(position, previousByte);

    int symbol = 1;
    for (int i = 7; i >= 0; i--)
    {
      uint matchBit = (uint)((matchByte >> 7) & 1);
      matchByte <<= 1;

      uint bit = (uint)((literal >> i) & 1);

      int probIndex = baseIndex + ((1 + (int)matchBit) << 8) + symbol;
      range.EncodeBit(ref Probs[probIndex], bit);

      symbol = (symbol << 1) | (int)bit;

      // Если первый же несовпавший бит встретился — остаток кодируем обычным деревом.
      if (matchBit != bit)
      {
        for (int j = i - 1; j >= 0; j--)
        {
          uint b2 = (uint)((literal >> j) & 1);
          range.EncodeBit(ref Probs[baseIndex + symbol], b2);
          symbol = (symbol << 1) | (int)b2;
        }

        break;
      }
    }
  }
}
