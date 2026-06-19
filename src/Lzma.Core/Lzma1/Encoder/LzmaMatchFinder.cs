namespace Lzma.Core.Lzma1;

/// <summary>
/// <para>
/// Простой match finder для LZMA: разбирает входные данные в последовательность
/// операций (литерал или match) методом хеш-цепочек с «жадным» (greedy) выбором.
/// </para>
/// <para>
/// Это первый рабочий парсер, дающий реальное сжатие. Он намеренно простой:
/// - хеш по 3 байтам;
/// - минимальная длина match — 3 байта (допустимо для LZMA, где минимум 2);
/// - жадный выбор самого длинного match в текущей позиции;
/// - дистанция ограничена размером словаря.
/// Оптимальный разбор (lazy/optimal parsing) — отдельный поздний шаг.
/// </para>
/// </summary>
internal static class LzmaMatchFinder
{
  /// <summary>Минимальная длина match, которую мы готовы кодировать.</summary>
  private const int MinMatch = 3;

  /// <summary>Сколько байт участвует в хеше (и минимальная длина совпадения для поиска).</summary>
  private const int HashBytes = 3;

  /// <summary>Число бит хеш-таблицы (65536 цепочек).</summary>
  private const int HashBits = 16;

  /// <summary>Размер хеш-таблицы (число цепочек). Нужен вызывающему для выделения буфера.</summary>
  internal const int HashTableSize = 1 << HashBits;

  /// <summary>Максимальная длина прохода по цепочке кандидатов в одной позиции.</summary>
  private const int MaxChainLength = 128;

  /// <summary>
  /// Разбирает <paramref name="input"/> в список LZMA-операций. Выделяет буфера сам —
  /// удобно для разовых вызовов и тестов.
  /// </summary>
  /// <param name="input">Исходные данные.</param>
  /// <param name="dictionarySize">Размер словаря; ограничивает максимальную дистанцию match.</param>
  public static List<LzmaEncodeOp> Parse(ReadOnlySpan<byte> input, int dictionarySize)
  {
    var ops = new List<LzmaEncodeOp>();
    int[] head = new int[HashTableSize];
    int[] prev = new int[Math.Max(1, input.Length)];
    Parse(input, dictionarySize, head, prev, ops);
    return ops;
  }

  /// <summary>
  /// Разбирает <paramref name="input"/> в <paramref name="ops"/>, переиспользуя переданные
  /// буфера <paramref name="head"/> (размер <see cref="HashTableSize"/>) и <paramref name="prev"/>
  /// (размер ≥ длины входа). Результат разбора идентичен аллоцирующей перегрузке — это нужно
  /// для покусочной обработки без аллокаций на каждый чанк.
  /// </summary>
  internal static void Parse(
      ReadOnlySpan<byte> input,
      int dictionarySize,
      int[] head,
      int[] prev,
      List<LzmaEncodeOp> ops)
  {
    ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dictionarySize);

    ops.Clear();

    int n = input.Length;
    if (n == 0)
      return;

    int maxMatch = LzmaConstants.MatchMaxLen;
    int maxDistance = dictionarySize;

    // Сброс только head: цепочки содержат лишь позиции, вставленные в этом проходе,
    // поэтому prev[candidate] всегда записан до чтения и в очистке не нуждается.
    Array.Fill(head, -1);

    int i = 0;
    while (i < n)
    {
      FindLongestMatch(
          input,
          i,
          maxMatch,
          maxDistance,
          head,
          prev,
          out int bestLength,
          out int bestDistance);

      if (bestLength >= MinMatch)
      {
        ops.Add(LzmaEncodeOp.Match(bestDistance, bestLength));

        int end = i + bestLength;
        while (i < end)
        {
          Insert(input, i, head, prev);
          i++;
        }
      }
      else
      {
        ops.Add(LzmaEncodeOp.Lit(input[i]));
        Insert(input, i, head, prev);
        i++;
      }
    }
  }

  /// <summary>
  /// Ищет самый длинный match для позиции <paramref name="pos"/> по хеш-цепочке.
  /// </summary>
  private static void FindLongestMatch(
      ReadOnlySpan<byte> input,
      int pos,
      int maxMatch,
      int maxDistance,
      int[] head,
      int[] prev,
      out int bestLength,
      out int bestDistance)
  {
    bestLength = 0;
    bestDistance = 0;

    int n = input.Length;

    // Для хеша нужно минимум HashBytes байт впереди.
    if (pos + HashBytes > n)
      return;

    int candidate = head[Hash(input, pos)];
    int chain = MaxChainLength;

    while (candidate >= 0 && chain-- > 0)
    {
      int distance = pos - candidate;

      // Цепочка упорядочена по убыванию позиции, поэтому дистанция только растёт:
      // как только вышли за словарь — дальше смысла нет.
      if (distance > maxDistance)
        break;

      int length = MatchLength(input, candidate, pos, maxMatch);

      if (length > bestLength)
      {
        bestLength = length;
        bestDistance = distance;

        if (length >= maxMatch)
          break;
      }

      candidate = prev[candidate];
    }
  }

  /// <summary>
  /// Возвращает длину совпадения между позициями <paramref name="source"/> и
  /// <paramref name="current"/> (source &lt; current). Совпадение может перекрываться.
  /// </summary>
  private static int MatchLength(
      ReadOnlySpan<byte> input,
      int source,
      int current,
      int maxMatch)
  {
    int n = input.Length;
    int length = 0;

    while (length < maxMatch
        && current + length < n
        && input[source + length] == input[current + length])
      length++;

    return length;
  }

  /// <summary>
  /// Вставляет позицию <paramref name="pos"/> в хеш-цепочку.
  /// </summary>
  private static void Insert(
      ReadOnlySpan<byte> input,
      int pos,
      int[] head,
      int[] prev)
  {
    // Последние (HashBytes - 1) байт не хешируются: для них нет полного ключа.
    if (pos + HashBytes > input.Length)
      return;

    int h = Hash(input, pos);
    prev[pos] = head[h];
    head[h] = pos;
  }

  /// <summary>
  /// Хеш по <see cref="HashBytes"/> байтам начиная с <paramref name="pos"/>.
  /// </summary>
  private static int Hash(ReadOnlySpan<byte> input, int pos)
  {
    uint value =
        ((uint)input[pos] << 16)
        | ((uint)input[pos + 1] << 8)
        | input[pos + 2];

    // Мультипликативное перемешивание Кнута.
    return (int)((value * 2654435761u) >> (32 - HashBits));
  }
}
