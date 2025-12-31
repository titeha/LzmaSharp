namespace Lzma.Core.Tests.Lzma1;

/// <summary>
/// <para>Тесты для <see cref="Core.Lzma1.LzmaLenDecoder"/>.</para>
/// <para>
/// Важно: тут мы НЕ проверяем "корректность LZMA на реальных потоках".
/// Мы проверяем локальную логику LenDecoder'а + корректную работу
/// поверх нашего RangeDecoder на искусственных, простых сценариях.
/// </para>
/// </summary>
public sealed class LzmaLenDecoderTests
{
  [Fact]
  public void LenDecoder_AllZeroBits_DecodesMinLength()
  {
    // Подготовка: код = 0x00000000 и поток данных из нулей.
    // При таких условиях RangeDecoder будет стабильно возвращать биты 0,
    // а LenDecoder должен отдать минимальную длину MatchMinLen.

    var lenDec = new Core.Lzma1.LzmaLenDecoder();
    lenDec.Reset(posStateCount: 1);

    var range = new Core.Lzma1.LzmaRangeDecoder();

    // 5 байт инициализации: 00 + 4 байта кода.
    // Далее добавим небольшой хвост нулей, чтобы RangeDecoder не упёрся в конец.
    byte[] src = new byte[5 + 32];
    src[0] = 0x00;
    // src[1..4] уже нули

    int offset = 0;
    Assert.Equal(Core.Lzma1.LzmaRangeInitResult.Ok, range.TryInitialize(src, ref offset));

    var res = lenDec.TryDecode(ref range, src, ref offset, posState: 0, out uint length);

    Assert.Equal(Core.Lzma1.LzmaRangeDecodeResult.Ok, res);
    Assert.Equal((uint)Core.Lzma1.LzmaConstants.MatchMinLen, length);
  }

  [Fact]
  public void LenDecoder_AllOneBits_DecodesMaxLength()
  {
    // Подготовка: код = 0xFFFFFFFF и поток из 0xFF.
    // В таком режиме RangeDecoder будет «стремиться» к битам 1,
    // что заставит LenDecoder выбрать ветку high и получить максимальную длину.

    var lenDec = new Core.Lzma1.LzmaLenDecoder();
    lenDec.Reset(posStateCount: 1);

    var range = new Core.Lzma1.LzmaRangeDecoder();

    byte[] src = new byte[5 + 64];
    src[0] = 0x00;
    src[1] = 0xFF;
    src[2] = 0xFF;
    src[3] = 0xFF;
    src[4] = 0xFF;

    for (int i = 5; i < src.Length; i++)
      src[i] = 0xFF;

    int offset = 0;
    Assert.Equal(Core.Lzma1.LzmaRangeInitResult.Ok, range.TryInitialize(src, ref offset));

    var res = lenDec.TryDecode(ref range, src, ref offset, posState: 0, out uint length);

    Assert.Equal(Core.Lzma1.LzmaRangeDecodeResult.Ok, res);
    Assert.Equal((uint)Core.Lzma1.LzmaConstants.MatchMaxLen, length);
  }

  [Fact]
  public void LenDecoder_AllOneBitsButNoPayload_ReturnsNeedMoreInput()
  {
    // Специально подаём только 5 байт инициализации.
    // Для «длинного» пути (high) LenDecoder потребуется больше входных данных
    // из-за нормализации RangeDecoder.

    var lenDec = new Core.Lzma1.LzmaLenDecoder();
    lenDec.Reset(posStateCount: 1);

    var range = new Core.Lzma1.LzmaRangeDecoder();

    byte[] src = [0x00, 0xFF, 0xFF, 0xFF, 0xFF];

    int offset = 0;
    Assert.Equal(Core.Lzma1.LzmaRangeInitResult.Ok, range.TryInitialize(src, ref offset));

    var res = lenDec.TryDecode(ref range, src, ref offset, posState: 0, out uint length);

    Assert.Equal(Core.Lzma1.LzmaRangeDecodeResult.NeedMoreInput, res);
    Assert.Equal(0u, length);
  }

  [Fact]
  public void LenDecoder_Reset_ValidatesPosStateCount()
  {
    var lenDec = new Core.Lzma1.LzmaLenDecoder();

    Assert.Throws<ArgumentOutOfRangeException>(() => lenDec.Reset(posStateCount: 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => lenDec.Reset(posStateCount: Core.Lzma1.LzmaConstants.NumPosStatesMax + 1));

    // Граничные значения должны приниматься.
    lenDec.Reset(posStateCount: 1);
    lenDec.Reset(posStateCount: Core.Lzma1.LzmaConstants.NumPosStatesMax);
  }

  [Fact]
  public void LenDecoder_TryDecode_ValidatesPosState()
  {
    var lenDec = new Core.Lzma1.LzmaLenDecoder();
    lenDec.Reset(posStateCount: 1);

    var range = new Core.Lzma1.LzmaRangeDecoder();

    byte[] src = new byte[5 + 32];
    src[0] = 0x00;

    int offset = 0;
    Assert.Equal(Core.Lzma1.LzmaRangeInitResult.Ok, range.TryInitialize(src, ref offset));

    // posState=1 при posStateCount=1 — недопустим.
    Assert.Throws<ArgumentOutOfRangeException>(() => _ = lenDec.TryDecode(ref range, src, ref offset, posState: 1, out _));
  }
}
