using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaFindMatchesTests
{
  private const int DictionarySize = 1 << 16;

  /// <summary>
  /// Строит хеш-цепочки по префиксу <paramref name="input"/> до <paramref name="pos"/> и
  /// возвращает кандидатов-совпадения в этой позиции.
  /// </summary>
  private static LzmaMatch[] CollectMatches(byte[] input, int pos, int windowSize)
  {
    int[] head = new int[LzmaMatchFinder.HashTableSize];
    Array.Fill(head, -1);
    int[] prev = new int[windowSize];
    int windowMask = windowSize - 1;

    for (int i = 0; i < pos; i++)
      LzmaMatchFinder.InsertCyclic(input, i, head, prev, windowMask);

    Span<LzmaMatch> buffer = new LzmaMatch[256];
    int count = LzmaMatchFinder.FindMatchesCyclic(
        input, pos, LzmaConstants.MatchMaxLen, DictionarySize, head, prev, windowMask, buffer);

    return buffer[..count].ToArray();
  }

  [Fact]
  public void Кандидаты_ИмеютСтрогоВозрастающуюДлинуИДистанцию()
  {
    // "abc...abcabc" — несколько разных по длине совпадений на разных дистанциях.
    byte[] input = Encoding.ASCII.GetBytes("abcabcabcabcXYZabcabcabcabcabc");

    for (int pos = 1; pos < input.Length; pos++)
    {
      LzmaMatch[] matches = CollectMatches(input, pos, 1 << 16);

      for (int k = 1; k < matches.Length; k++)
      {
        Assert.True(matches[k].Length > matches[k - 1].Length,
            $"pos={pos}: длина должна строго возрастать.");
        Assert.True(matches[k].Distance > matches[k - 1].Distance,
            $"pos={pos}: дистанция должна строго возрастать.");
      }
    }
  }

  [Fact]
  public void Кандидаты_ДействительноСовпадаютСИсходнымиБайтами()
  {
    byte[] input = Encoding.ASCII.GetBytes("the quick brown fox the quick brown fox the quick");

    for (int pos = 1; pos < input.Length; pos++)
    {
      LzmaMatch[] matches = CollectMatches(input, pos, 1 << 16);

      foreach (LzmaMatch m in matches)
      {
        Assert.InRange(m.Distance, 1, pos);
        Assert.True(m.Length >= 1);

        // Байты на дистанции m.Distance назад совпадают на длину m.Length.
        for (int k = 0; k < m.Length; k++)
          Assert.Equal(input[pos - m.Distance + k], input[pos + k]);
      }
    }
  }

  [Fact]
  public void ПоследнийКандидат_СамыйДлинный()
  {
    byte[] input = Encoding.ASCII.GetBytes("AAAAAAAAAAAAAAAAAAAA bca bca bcabca");

    for (int pos = 1; pos < input.Length; pos++)
    {
      LzmaMatch[] matches = CollectMatches(input, pos, 1 << 16);
      if (matches.Length == 0)
        continue;

      int maxLen = matches.Max(m => m.Length);
      Assert.Equal(maxLen, matches[^1].Length);
    }
  }

  [Fact]
  public void БезСовпадений_ПустойСписок()
  {
    // Все байты уникальны — совпадений нет.
    byte[] input = new byte[200];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)i;

    for (int pos = 1; pos < input.Length; pos++)
    {
      LzmaMatch[] matches = CollectMatches(input, pos, 1 << 16);
      Assert.Empty(matches);
    }
  }
}
