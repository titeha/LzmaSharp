using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaMatchFinderTests
{
  private const int DictionarySize = 1 << 16;

  [Fact]
  public void Parse_ПустойВход_ДаётПустойСписок()
  {
    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse([], DictionarySize);

    Assert.Empty(ops);
  }

  [Theory]
  [InlineData("A")]
  [InlineData("AB")]
  [InlineData("AAAAA")]
  [InlineData("ABCABCABC")]
  [InlineData("the quick brown fox the quick brown fox")]
  public void Parse_РеконструкцияИзОперацийРавнаВходу_ДляТекста(string text)
  {
    byte[] input = Encoding.ASCII.GetBytes(text);

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    AssertOpsAreValid(ops, DictionarySize);
    Assert.Equal(input, Reconstruct(ops));
  }

  [Fact]
  public void Parse_РеконструкцияРавнаВходу_ДляСлучайныхДанных()
  {
    byte[] input = MakePseudoRandom(4096, seed: 12345);

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    AssertOpsAreValid(ops, DictionarySize);
    Assert.Equal(input, Reconstruct(ops));
  }

  [Fact]
  public void Parse_РеконструкцияРавнаВходу_ДляПовторяющихсяДанных()
  {
    byte[] input = MakeRepeated("ABCD", 250);

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    AssertOpsAreValid(ops, DictionarySize);
    Assert.Equal(input, Reconstruct(ops));
  }

  [Fact]
  public void Parse_ПовторяющиесяБайты_ДаютMatchОперации()
  {
    byte[] input = Encoding.ASCII.GetBytes(new string('A', 100));

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    Assert.Contains(ops, op => op.Kind == LzmaEncodeOpKind.Match);
  }

  [Fact]
  public void Parse_УникальныеБайты_ДаютТолькоЛитералы()
  {
    byte[] input = new byte[256];
    for (int i = 0; i < input.Length; i++)
      input[i] = (byte)i;

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    Assert.All(ops, op => Assert.Equal(LzmaEncodeOpKind.Literal, op.Kind));
    Assert.Equal(input.Length, ops.Count);
  }

  [Fact]
  public void Parse_ДлинаMatchНеПревышаетМаксимум()
  {
    byte[] input = Encoding.ASCII.GetBytes(new string('Z', 1000));

    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, DictionarySize);

    AssertOpsAreValid(ops, DictionarySize);
    Assert.Equal(input, Reconstruct(ops));

    Assert.All(
        ops.Where(op => op.Kind == LzmaEncodeOpKind.Match),
        op => Assert.True(op.Length <= LzmaConstants.MatchMaxLen));
  }

  private static void AssertOpsAreValid(IReadOnlyList<LzmaEncodeOp> ops, int dictionarySize)
  {
    int produced = 0;

    foreach (LzmaEncodeOp op in ops)
    {
      if (op.Kind == LzmaEncodeOpKind.Literal)
      {
        produced++;
        continue;
      }

      Assert.True(op.Length >= LzmaConstants.MatchMinLen);
      Assert.True(op.Length <= LzmaConstants.MatchMaxLen);
      Assert.True(op.Distance >= 1);
      Assert.True(op.Distance <= dictionarySize);

      // Дистанция не может ссылаться дальше уже произведённых байт.
      Assert.True(op.Distance <= produced);

      produced += op.Length;
    }
  }

  private static byte[] Reconstruct(IReadOnlyList<LzmaEncodeOp> ops)
  {
    var output = new List<byte>();

    foreach (LzmaEncodeOp op in ops)
    {
      if (op.Kind == LzmaEncodeOpKind.Literal)
      {
        output.Add(op.Literal);
        continue;
      }

      int start = output.Count - op.Distance;

      for (int k = 0; k < op.Length; k++)
        output.Add(output[start + k]);
    }

    return [.. output];
  }

  private static byte[] MakeRepeated(string unit, int times)
  {
    var builder = new StringBuilder(unit.Length * times);

    for (int i = 0; i < times; i++)
      builder.Append(unit);

    return Encoding.ASCII.GetBytes(builder.ToString());
  }

  private static byte[] MakePseudoRandom(int length, int seed)
  {
    var random = new Random(seed);
    byte[] data = new byte[length];
    random.NextBytes(data);
    return data;
  }
}
