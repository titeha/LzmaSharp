using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

public sealed class LzmaDecoderLiteralOnlyTests
{
  [Fact]
  public void Decode_OneShot_Produces_Exact_Output()
  {
    // LZMA props: lc=3, lp=0, pb=2 => propsByte = 3 + 0*9 + 2*45 = 93.
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    // Для literal-only тестов размер словаря не критичен, но пусть будет "как в жизни".
    const int dictionarySize = 1 << 16;

    byte[] expected = System.Text.Encoding.ASCII.GetBytes("Hello, LZMA literal-only!\n");
    byte[] compressed = LzmaTestLiteralOnlyEncoder.Encode(props, expected);

    var decoder = new LzmaDecoder(props, dictionarySize);

    byte[] output = new byte[expected.Length];
    LzmaDecodeResult res = decoder.Decode(
        compressed,
        out int consumed,
        output,
        out int written,
        out var progress);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(expected.Length, written);
    Assert.True(consumed > 0); // хоть что-то должны были прочитать
    Assert.Equal(expected, output);

    // Прогресс должен быть консистентен.
    Assert.Equal(consumed, progress.BytesRead);
    Assert.Equal(written, progress.BytesWritten);
  }

  [Fact]
  public void Decode_Works_When_Input_And_Output_Are_Streamed_In_Tiny_Chunks()
  {
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    const int dictionarySize = 1 << 16;

    // Немного длиннее, чтобы было больше шансов попасть на границы нормализации range coder.
    byte[] expected = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
    byte[] compressed = LzmaTestLiteralOnlyEncoder.Encode(props, expected);

    var decoder = new LzmaDecoder(props, dictionarySize);

    var outputAll = new List<byte>(expected.Length);

    int inOffset = 0;
    int watchdog = 0;

    // CA2014: stackalloc не внутри цикла.
    Span<byte> outChunk = stackalloc byte[7];

    while (outputAll.Count < expected.Length)
    {
      watchdog++;
      Assert.True(watchdog < 10000, "Похоже на зависание: декодер не продвигается.");

      // Дадим вход маленькими кусками 1..3 байта.
      int takeIn = Math.Min(1 + (outputAll.Count % 3), compressed.Length - inOffset);
      if (takeIn <= 0)// Сжатый поток закончился, а выход ещё нет => ошибка.
        throw new InvalidOperationException("Вход закончился раньше, чем мы получили весь ожидаемый выход.");

      ReadOnlySpan<byte> inChunk = compressed.AsSpan(inOffset, takeIn);

      // Выход тоже маленькими кусками.
      LzmaDecodeResult res = decoder.Decode(inChunk, out int consumed, outChunk, out int written, out _);

      inOffset += consumed;

      if (written > 0)
        outputAll.AddRange(outChunk.Slice(0, written).ToArray());

      switch (res)
      {
        case LzmaDecodeResult.Ok:
        case LzmaDecodeResult.NeedsMoreInput:
          // Оба результата допустимы: либо упёрлись в маленький dst, либо ждём ещё src.
          break;

        default:
          throw new InvalidOperationException($"Неожиданный результат декодирования: {res}");
      }
    }

    Assert.Equal(expected, outputAll.ToArray());
  }

  [Fact]
  public void Decode_Returns_NotImplemented_When_It_Sees_Rep1_OrHigher()
  {
    const byte propsByte = 93;
    Assert.True(LzmaProperties.TryParse(propsByte, out var props));

    const int dictionarySize = 1 << 16;

    // Кодируем для первого символа: isMatch=1 (match), isRep=1 (rep), isRepG0=1 (rep1/rep2/rep3).
    // Rep1+ пока не реализованы => декодер должен вернуть NotImplemented.
    var range = new LzmaTestRangeEncoder();

    int numPosStates = 1 << props.Pb;

    ushort[] isMatch = new ushort[LzmaConstants.NumStates * numPosStates];
    ushort[] isRep = new ushort[LzmaConstants.NumStates];
    ushort[] isRepG0 = new ushort[LzmaConstants.NumStates];

    LzmaProbability.Reset(isMatch);
    LzmaProbability.Reset(isRep);
    LzmaProbability.Reset(isRepG0);

    var state = new LzmaState();
    state.Reset();

    // isMatch
    ref ushort pIsMatch = ref isMatch[state.Value * numPosStates + 0 /*posState*/];
    range.EncodeBit(ref pIsMatch, 1);

    // isRep
    ref ushort pIsRep = ref isRep[state.Value];
    range.EncodeBit(ref pIsRep, 1);

    // isRepG0
    ref ushort pIsRepG0 = ref isRepG0[state.Value];
    range.EncodeBit(ref pIsRepG0, 1);

    byte[] compressed = range.Finish();

    var decoder = new LzmaDecoder(props, dictionarySize);

    Span<byte> out1 = stackalloc byte[1];
    LzmaDecodeResult res = decoder.Decode(compressed, out _, out1, out _, out _);

    Assert.Equal(LzmaDecodeResult.NotImplemented, res);
  }
}
