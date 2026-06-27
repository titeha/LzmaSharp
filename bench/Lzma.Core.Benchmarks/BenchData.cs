namespace Lzma.Core.Benchmarks;

/// <summary>
/// Общие тестовые данные для бенчмарков и отчётов (чтобы измерения скорости и степени
/// сжатия шли на одном и том же входе).
/// </summary>
internal static class BenchData
{
  private static readonly string[] Words =
  [
    "the", "quick", "brown", "fox", "jumps", "over", "lazy", "dog", "lorem", "ipsum",
    "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "do", "eiusmod",
    "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", "data",
    "compression", "algorithm", "stream", "buffer", "window", "match", "literal", "range",
  ];

  /// <summary>
  /// Детерминированные полу-реалистичные текстовые данные: слова из небольшого словаря,
  /// разделённые пробелами и переводами строк. Хорошо сжимаются (как реальный текст),
  /// поэтому match finder реально работает.
  /// </summary>
  public static byte[] MakeTextLike(int size)
  {
    var output = new byte[size];
    var random = new Random(20260619);
    int pos = 0;
    int wordsOnLine = 0;

    while (pos < size)
    {
      string word = Words[random.Next(Words.Length)];
      foreach (char c in word)
      {
        if (pos >= size)
          break;
        output[pos++] = (byte)c;
      }

      if (pos < size)
        output[pos++] = (byte)' ';

      if (++wordsOnLine >= 12 && pos < size)
      {
        output[pos++] = (byte)'\n';
        wordsOnLine = 0;
      }
    }

    return output;
  }

  /// <summary>
  /// Детерминированные «x86-подобные» данные: случайный фон + регулярные ветвления
  /// E8/E9 (CALL/JMP) с короткими смещениями. Для бенчмарка BCJ2 (детект ветвлений,
  /// конвертация адресов, range-кодер).
  /// </summary>
  public static byte[] MakeX86Like(int size)
  {
    var output = new byte[size];
    var random = new Random(20260627);
    random.NextBytes(output);

    // Каждые ~24 байта вставляем настоящее ветвление с коротким смещением (конвертируемое).
    for (int i = 16; i + 8 < size; i += 24)
    {
      output[i] = (i % 2 == 0) ? (byte)0xE8 : (byte)0xE9;
      int rel = (i * 7) % 4096;
      output[i + 1] = (byte)rel;
      output[i + 2] = (byte)(rel >> 8);
      output[i + 3] = 0;
      output[i + 4] = 0;
    }

    return output;
  }

  /// <summary>
  /// Детерминированные структурированные данные с повторяющимися фрагментами на
  /// регулярных дистанциях (как логи/таблицы/код). Здесь rep-матчи реально помогают.
  /// </summary>
  public static byte[] MakePeriodic(int size)
  {
    string[] templates =
    [
      "2026-06-19 12:00:00 [INFO ] request id=000000 user=alice status=200 path=/api/v1/items\n",
      "2026-06-19 12:00:00 [WARN ] request id=000000 user=bobby status=404 path=/api/v1/items\n",
      "2026-06-19 12:00:00 [ERROR] request id=000000 user=carol status=500 path=/api/v1/items\n",
    ];

    var output = new byte[size];
    var random = new Random(20260619);
    int pos = 0;

    while (pos < size)
    {
      byte[] line = System.Text.Encoding.ASCII.GetBytes(templates[random.Next(templates.Length)]);
      int take = Math.Min(line.Length, size - pos);
      Array.Copy(line, 0, output, pos, take);
      pos += take;
    }

    return output;
  }
}
