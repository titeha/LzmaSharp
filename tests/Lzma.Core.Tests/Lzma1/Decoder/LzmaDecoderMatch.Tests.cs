using Lzma.Core.Lzma1;
using Lzma.Core.Tests.Helpers;

namespace Lzma.Core.Tests.Lzma1;

/// <summary>
/// <para>Минимальный тест на обычный match (isRep == 0).</para>
/// <para>
/// Важно: это не «реальные архивы», а маленький синтетический поток,
/// который мы сами кодируем через тестовый range encoder.
/// </para>
/// <para>
/// Цель — проверить:
/// - декодер умеет путь isMatch == 1;
/// - умеет декодировать длину (len) и расстояние (distance);
/// - умеет копировать match из словаря в выход.
/// </para>
/// </summary>
public sealed class LzmaDecoderMatchTests
{
  [Fact]
  public void Decode_SimpleMatch_Distance1_RepeatsByte()
  {
    Assert.True(LzmaProperties.TryParse(0x5D, out var props));

    // План:
    // 1) выдаём один литерал 'A'
    // 2) затем match длиной 8, distance = 1 => получим "AAAAAAAAA" (1 + 8)
    //
    // Для простоты тестовый энкодер умеет только distance = 1
    // и длины 2..9 (len в low-части len decoder).
    byte[] lzma = LzmaTestSimpleMatchEncoder.Encode(props, totalLen: 9);

    var dec = new LzmaDecoder(props, dictionarySize: 1 << 16);

    byte[] dst = new byte[9];
    var res = dec.Decode(lzma, out int consumed, dst, out int written, out var prog);

    Assert.Equal(LzmaDecodeResult.Ok, res);
    Assert.Equal(dst.Length, written);

    for (int i = 0; i < dst.Length; i++)
      Assert.Equal((byte)'A', dst[i]);

    // Прогресс в конце вызова — «сколько мы реально съели» и «сколько реально записали».
    Assert.Equal(consumed, prog.BytesRead);
    Assert.Equal(written, prog.BytesWritten);
  }
}
