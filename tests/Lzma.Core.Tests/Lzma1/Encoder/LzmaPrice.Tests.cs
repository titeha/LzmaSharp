using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaPriceTests
{
  // Цена одного бита в единицах фиксированной точки (1 << 4).
  private const int BitPrice = 16;

  [Fact]
  public void Price_ПриВероятностиПоУмолчанию_ПримерноОдинБит()
  {
    // Начальная вероятность 1/2 (1024): любой бит должен стоить ≈ 1 бит = 16 единиц.
    ushort half = LzmaConstants.ProbabilityInitValue;

    Assert.InRange(LzmaPrice.Price0(half), BitPrice - 2u, BitPrice + 2u);
    Assert.InRange(LzmaPrice.Price1(half), BitPrice - 2u, BitPrice + 2u);
  }

  [Fact]
  public void Price0_УбываетСРостомВероятности()
  {
    // Чем выше prob, тем «увереннее» модель в бите 0 — тем дешевле его кодировать.
    uint prev = uint.MaxValue;

    for (int prob = 16; prob < LzmaConstants.BitModelTotal; prob += 16)
    {
      uint price = LzmaPrice.Price0((ushort)prob);
      Assert.True(price <= prev, $"Цена должна убывать: prob={prob}, price={price}, prev={prev}.");
      prev = price;
    }
  }

  [Fact]
  public void Price1_СимметричнаPrice0()
  {
    // Бит 1 при вероятности p стоит столько же, сколько бит 0 при (Total-1-p).
    for (int prob = 16; prob < LzmaConstants.BitModelTotal; prob += 16)
    {
      uint p1 = LzmaPrice.Price1((ushort)prob);
      uint p0Mirror = LzmaPrice.Price0((ushort)(LzmaConstants.BitModelTotal - 1 - prob));
      Assert.Equal(p0Mirror, p1);
    }
  }

  [Fact]
  public void BitTreePrice_РавнаСуммеЦенБитПоПути()
  {
    // Дерево из 3 бит: проверяем, что цена символа = сумма цен битов по обходу
    // (тот же обход индексов, что и в LzmaBitTreeEncoder.EncodeSymbol).
    const int numBits = 3;
    ushort[] probs = new ushort[1 << numBits];
    for (int i = 0; i < probs.Length; i++)
      probs[i] = (ushort)(200 + i * 137); // произвольные, но различимые вероятности

    for (uint symbol = 0; symbol < (1u << numBits); symbol++)
    {
      uint expected = 0;
      int index = 1;
      for (int bitIndex = numBits; bitIndex != 0; bitIndex--)
      {
        uint bit = (symbol >> (bitIndex - 1)) & 1u;
        expected += LzmaPrice.Price(probs[index], bit);
        index = (index << 1) + (int)bit;
      }

      Assert.Equal(expected, LzmaPrice.BitTreePrice(probs, numBits, symbol));
    }
  }

  [Fact]
  public void BitTreeReversePrice_РавнаСуммеЦенБитПоПути()
  {
    const int numBits = 4;
    ushort[] probs = new ushort[1 << numBits];
    for (int i = 0; i < probs.Length; i++)
      probs[i] = (ushort)(100 + i * 91);

    for (uint symbol = 0; symbol < (1u << numBits); symbol++)
    {
      uint expected = 0;
      int index = 1;
      uint s = symbol;
      for (int i = 0; i < numBits; i++)
      {
        uint bit = s & 1u;
        expected += LzmaPrice.Price(probs[index], bit);
        index = (index << 1) + (int)bit;
        s >>= 1;
      }

      Assert.Equal(expected, LzmaPrice.BitTreeReversePrice(probs, numBits, symbol));
    }
  }
}
