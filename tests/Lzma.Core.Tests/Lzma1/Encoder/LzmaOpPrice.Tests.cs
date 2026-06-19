using System.Text;

using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaOpPriceTests
{
  private const int Dict = 1 << 16;

  private static LzmaProperties Props => new(Lc: 3, Lp: 0, Pb: 2);

  /// <summary>
  /// Прайсит каждую операцию ДО кодирования (по текущему состоянию модели), затем
  /// кодирует её. Сумма предсказанных цен (в битах) должна совпасть с реальным размером
  /// закодированного потока с небольшой погрешностью — это и есть критерий пригодности
  /// ценовой модели для optimal parsing.
  /// </summary>
  private static void AssertPriceMatchesOutput(byte[] input, double tolerance)
  {
    List<LzmaEncodeOp> ops = LzmaMatchFinder.Parse(input, Dict);

    var enc = new LzmaEncoder(Props, Dict);

    ulong totalPrice = 0;
    foreach (LzmaEncodeOp op in ops)
    {
      totalPrice += enc.PriceOp(op);
      enc.EncodeOp(op);
    }

    byte[] payload = enc.FinishChunk();

    // Цена в единицах (бит << 4); делим на 16 (бит) и на 8 (байт) => /128.
    double predictedBytes = totalPrice / 128.0;
    double actualBytes = payload.Length;

    Assert.InRange(predictedBytes, actualBytes * (1 - tolerance), actualBytes * (1 + tolerance));
  }

  [Fact]
  public void Цена_ПримерноРавнаРазмеру_ДляТекста()
  {
    byte[] input = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat(
            "The quick brown fox jumps over the lazy dog. Съешь ещё мягких булок. ", 200)));

    AssertPriceMatchesOutput(input, tolerance: 0.05);
  }

  [Fact]
  public void Цена_ПримерноРавнаРазмеру_ДляПовторяющихсяДанных()
  {
    // Периодические данные — много rep-матчей: проверяем и ценообразование reps.
    byte[] input = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ABCDABCD1234 ", 800)));

    AssertPriceMatchesOutput(input, tolerance: 0.05);
  }

  [Fact]
  public void Цена_ПримерноРавнаРазмеру_ДляПсевдослучайныхДанных()
  {
    var rnd = new Random(20260619);
    byte[] input = new byte[16000];
    rnd.NextBytes(input);

    // Несжимаемые данные — почти все литералы; цена должна быть близка к размеру.
    AssertPriceMatchesOutput(input, tolerance: 0.05);
  }
}
