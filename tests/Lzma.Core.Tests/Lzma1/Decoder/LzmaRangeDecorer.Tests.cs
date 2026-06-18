using Lzma.Core.Lzma1;

namespace Lzma.Core.Tests.Lzma1;

/// <summary>
/// <para>Тесты на «самую базу» range coder'а.</para>
/// <para>
/// Здесь мы специально НЕ тестируем весь LZMA-декодер (его ещё нет).
/// Но range decoder — фундамент для следующего шага (LZMA-чанки в LZMA2),
/// поэтому мы проверяем:
/// - корректность инкрементальной инициализации (5 байт);
/// - корректность декодирования одного бита (оба ветвления);
/// - корректное поведение в ситуации, когда требуется нормализация, но входа нет.
/// </para>
/// </summary>
public sealed class LzmaRangeDecoderTests
{
  [Fact]
  public void TryInitialize_МожноРазбитьНаНесколькоВызовов()
  {
    var rd = new LzmaRangeDecoder();
    rd.Reset();

    // 5 байт инициализации: 11 22 33 44 55.
    // В uint после 5 байт сохраняются последние 4 байта => 22 33 44 55.
    ReadOnlySpan<byte> part1 = [0x11, 0x22];
    int off1 = 0;
    Assert.Equal(LzmaRangeInitResult.NeedMoreInput, rd.TryInitialize(part1, ref off1));
    Assert.Equal(2, off1);
    Assert.Equal(3, rd.InitBytesRemaining);
    Assert.False(rd.IsInitialized);

    ReadOnlySpan<byte> part2 = [0x33, 0x44, 0x55];
    int off2 = 0;
    Assert.Equal(LzmaRangeInitResult.Ok, rd.TryInitialize(part2, ref off2));
    Assert.Equal(3, off2);
    Assert.Equal(0, rd.InitBytesRemaining);
    Assert.True(rd.IsInitialized);

    Assert.Equal(0xFFFF_FFFFu, rd.Range);
    Assert.Equal(0x2233_4455u, rd.Code);
  }

  [Fact]
  public void DecodeBit_ВеткаНоль_ОбновляетВероятностьИRange()
  {
    var rd = Init([0, 0, 0, 0, 0]);

    ushort prob = 1024;
    int off = 0;

    var res = rd.TryDecodeBit(ref prob, [], ref off, out uint bit);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(0u, bit);
    Assert.Equal(0, off);

    // prob += (2048 - prob) >> 5 => 1024 + 32 = 1056
    Assert.Equal((ushort)1056, prob);

    // bound = (0xFFFF_FFFF >> 11) * 1024 = 0x001F_FFFF * 1024 = 0x7FFF_FC00
    Assert.Equal(0x7FFF_FC00u, rd.Range);
    Assert.Equal(0u, rd.Code);
  }

  [Fact]
  public void DecodeBit_ВеткаЕдиница_ОбновляетВероятностьRangeИCode()
  {
    var rd = Init([0xFF, 0xFF, 0xFF, 0xFF, 0xFF]);

    ushort prob = 1024;
    int off = 0;

    var res = rd.TryDecodeBit(ref prob, [], ref off, out uint bit);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(1u, bit);
    Assert.Equal(0, off);

    // prob -= prob >> 5 => 1024 - 32 = 992
    Assert.Equal((ushort)992, prob);

    // Range/Code после вычитания bound.
    Assert.Equal(0x8000_03FFu, rd.Range);
    Assert.Equal(0x8000_03FFu, rd.Code);
  }

  [Fact]
  public void DecodeBit_ЕслиНужнаНормализация_БезВходаВозвращаетNeedMoreInput_ИНеМеняетСостояние()
  {
    var rd = Init([0, 0, 0, 0, 0]);

    // Уменьшаем Range так, чтобы потребовалась нормализация.
    // Наш TryDecodeDirectBits НЕ нормализует на выходе, поэтому после 8 бит
    // Range станет 0x00FF_FFFF (< TopValue).
    int off = 0;
    Assert.Equal(LzmaRangeDecodeResult.Ok, rd.TryDecodeDirectBits(8, [], ref off, out _));
    Assert.Equal(0, off);
    Assert.True(rd.Range < LzmaRangeDecoder.TopValue);

    var rangeBefore = rd.Range;
    var codeBefore = rd.Code;

    ushort prob = 1024;
    var probBefore = prob;

    int off2 = 0;
    var res = rd.TryDecodeBit(ref prob, [], ref off2, out _);

    Assert.Equal(LzmaRangeDecodeResult.NeedMoreInput, res);
    Assert.Equal(0, off2);
    Assert.Equal(probBefore, prob);
    Assert.Equal(rangeBefore, rd.Range);
    Assert.Equal(codeBefore, rd.Code);

    // Теперь даём один байт — этого достаточно, чтобы выполнить нормализацию,
    // после чего декодирование сможет продолжиться.
    ReadOnlySpan<byte> extra = [0xAA];
    off2 = 0;

    res = rd.TryDecodeBit(ref prob, extra, ref off2, out _);

    Assert.Equal(LzmaRangeDecodeResult.Ok, res);
    Assert.Equal(1, off2); // один байт был потреблён на нормализацию
  }

  private static LzmaRangeDecoder Init(byte[] initBytes)
  {
    if (initBytes is null)
      throw new ArgumentNullException(nameof(initBytes));
    if (initBytes.Length != 5)
      throw new ArgumentException("Нужно ровно 5 байт для Init().", nameof(initBytes));

    var rd = new LzmaRangeDecoder();
    rd.Reset();

    int off = 0;
    Assert.Equal(LzmaRangeInitResult.Ok, rd.TryInitialize(initBytes, ref off));
    Assert.Equal(5, off);
    Assert.True(rd.IsInitialized);

    return rd;
  }
}
