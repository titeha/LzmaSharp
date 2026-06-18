using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaBitTreeDecoderTests
{
  [Fact]
  public void Reset_ЗаполняетВероятностиНачальнымЗначением()
  {
    var bt = new LzmaBitTreeDecoder(numBits: 5);

    // Индекс 0 в BitTree не используется, но мы всё равно его инициализируем,
    // чтобы поведение было максимально предсказуемым.
    for (int i = 0; i < bt.ProbabilityCount; i++)
      Assert.Equal(LzmaProbability.Initial, bt.GetProbability(i));
  }

  [Fact]
  public void DecodeSymbol_ПриНулевомКодеДолженПолучитьсяНулевойСимвол()
  {
    // Init bytes для RangeDecoder: первый байт обязан быть 0, дальше 4 байта code.
    // Если code == 0, то при стартовых prob=1024 почти все биты будут 0.
    ReadOnlySpan<byte> init = [0x00, 0x00, 0x00, 0x00, 0x00];

    var range = new LzmaRangeDecoder();
    int consumed = 0;
    Assert.Equal(LzmaRangeInitResult.Ok, range.TryInitialize(init, ref consumed));
    Assert.Equal(5, consumed);

    var bt = new LzmaBitTreeDecoder(numBits: 5);

    int offset = 0;
    var res = bt.TryDecodeSymbol(ref range, [], ref offset, out uint symbol);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal<uint>(0, symbol);
    Assert.Equal(0, offset); // нормализация не потребовалась

    // Проверяем, что вероятности по пути 1,2,4,8,16 изменились в сторону "0".
    Assert.Equal((ushort)(LzmaProbability.Initial + 32), bt.GetProbability(1));
    Assert.Equal((ushort)(LzmaProbability.Initial + 32), bt.GetProbability(2));
    Assert.Equal((ushort)(LzmaProbability.Initial + 32), bt.GetProbability(4));
    Assert.Equal((ushort)(  LzmaProbability.Initial + 32), bt.GetProbability(8));
    Assert.Equal((ushort)(LzmaProbability.Initial + 32), bt.GetProbability(16));
  }

  [Fact]
  public void DecodeSymbol_ПриМаксимальномКодеДолженПолучитьсяМаксимальныйСимвол()
  {
    // code == 0xFFFFFFFF в большинстве случаев тянет декодер в ветки "1".
    ReadOnlySpan<byte> init = [0x00, 0xFF, 0xFF, 0xFF, 0xFF];

    var range = new LzmaRangeDecoder();
    int consumed = 0;
    Assert.Equal(LzmaRangeInitResult.Ok, range.TryInitialize(init, ref consumed));
    Assert.Equal(5, consumed);

    var bt = new LzmaBitTreeDecoder(numBits: 5);

    int offset = 0;
    var res = bt.TryDecodeSymbol(ref range, [], ref offset, out uint symbol);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal<uint>((1u << 5) - 1, symbol);
    Assert.Equal(0, offset);

    // Ветвь "1" уменьшает вероятность (приводя её к меньшим значениям).
    // Для numBits=5 путь: 1,3,7,15,31.
    Assert.Equal((ushort)(LzmaProbability.Initial - 32), bt.GetProbability(1));
    Assert.Equal((ushort)(LzmaProbability.Initial - 32), bt.GetProbability(3));
    Assert.Equal((ushort)(LzmaProbability.Initial - 32), bt.GetProbability(7));
    Assert.Equal((ushort)(LzmaProbability.Initial - 32), bt.GetProbability(15));
    Assert.Equal((ushort)(LzmaProbability.Initial - 32), bt.GetProbability(31));
  }

  [Fact]
  public void ReverseDecode_НаКраяхТожеРаботает()
  {
    // Этот тест не пытается доказать корректность "обратности" на сложных паттернах,
    // но гарантирует базовую работоспособность и интеграцию с RangeDecoder.

    // 1) Все нули -> 0
    {
      ReadOnlySpan<byte> init = [0x00, 0x00, 0x00, 0x00, 0x00];
      var range = new LzmaRangeDecoder();
      int consumed = 0;
      Assert.Equal(LzmaRangeInitResult.Ok, range.TryInitialize(init, ref consumed));

      var bt = new LzmaBitTreeDecoder(numBits: 4);
      int offset = 0;
      var res = bt.TryReverseDecode(ref range, [], ref offset, out uint symbol);
      Assert.Equal(LzmaRangeDecodeResult.Ok, res);
      Assert.Equal<uint>(0, symbol);
    }

    // 2) Все единицы -> (1<<numBits)-1
    {
      ReadOnlySpan<byte> init = [0x00, 0xFF, 0xFF, 0xFF, 0xFF];
      var range = new LzmaRangeDecoder();
      int consumed = 0;
      Assert.Equal(LzmaRangeInitResult.Ok, range.TryInitialize(init, ref consumed));

      var bt = new LzmaBitTreeDecoder(numBits: 4);
      int offset = 0;
      var res = bt.TryReverseDecode(ref range, [], ref offset, out uint symbol);
      Assert.Equal(LzmaRangeDecodeResult.Ok, res);
      Assert.Equal((1u << 4) - 1, symbol);
    }
  }
}
