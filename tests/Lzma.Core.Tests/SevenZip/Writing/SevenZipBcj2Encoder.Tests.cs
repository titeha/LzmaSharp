using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcj2EncoderTests
{
  // encode → decode (нашим декодером) → должно совпасть с исходным входом.
  private static void AssertRoundTrip(byte[] input)
  {
    SevenZipBcj2Streams s = SevenZipBcj2Encoder.Encode(input);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        s.Main, s.Call, s.Jump, s.Control, input.Length, out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(input, output);
  }

  [Fact]
  public void RoundTrip_ПустойВход()
  {
    AssertRoundTrip([]);
  }

  [Fact]
  public void RoundTrip_БезВетвлений()
  {
    byte[] input = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x10, 0x20];
    AssertRoundTrip(input);
  }

  [Fact]
  public void RoundTrip_E8_КонвертируемыйВызов_КонвертацияПроисходит()
  {
    // opcode E8 в индексе 1, disp (LE) = 2 → abs = 2 + 2 + 4 = 8, в пределах файла.
    byte[] input = [0x90, 0xE8, 0x02, 0x00, 0x00, 0x00, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

    SevenZipBcj2Streams s = SevenZipBcj2Encoder.Encode(input);

    Assert.Equal(4, s.Call.Length); // ветвление реально сконвертировано (4 байта abs)
    Assert.Empty(s.Jump);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        s.Main, s.Call, s.Jump, s.Control, input.Length, out byte[] output);
    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(input, output);
  }

  [Fact]
  public void RoundTrip_E9_Прыжок()
  {
    byte[] input = [0x00, 0x00, 0xE9, 0x04, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55];
    AssertRoundTrip(input);
  }

  [Fact]
  public void RoundTrip_Jcc_0F8x()
  {
    // 0F 80 — условный переход с 4-байтовым смещением.
    byte[] input = [0xAA, 0x0F, 0x80, 0x03, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
    AssertRoundTrip(input);
  }

  [Fact]
  public void RoundTrip_ВетвлениеВКонце_БезDisp()
  {
    // E8 как последний байт — конвертировать нельзя, бит не пишется.
    byte[] input = [0x00, 0x11, 0x22, 0xE8];
    AssertRoundTrip(input);
  }

  [Fact]
  public void RoundTrip_ВетвлениеСНедостаткомБайт()
  {
    // E8, но после него только 2 байта — не конвертируется.
    byte[] input = [0x00, 0xE8, 0x01, 0x02];
    AssertRoundTrip(input);
  }

  [Fact]
  public void RoundTrip_ПодрядИдущиеE8()
  {
    byte[] input = new byte[64];
    for (int i = 0; i < input.Length; i++)
      input[i] = 0xE8;

    AssertRoundTrip(input);
  }

  [Theory]
  [InlineData(0xDEAD_BEEFu, 1)]
  [InlineData(0x1234_5678u, 4096)]
  [InlineData(0x0BADF00Du, 65536)]
  public void RoundTrip_СлучайныеДанные(uint seed, int length)
  {
    byte[] input = new byte[length];
    uint x = seed;

    for (int i = 0; i < length; i++)
    {
      x ^= x << 13;
      x ^= x >> 17;
      x ^= x << 5;
      input[i] = (byte)x;
    }

    AssertRoundTrip(input);
  }

  [Theory]
  [InlineData(0xCAFEBABEu, 20000)]
  public void RoundTrip_СмесьКодаИВетвлений(uint seed, int length)
  {
    // Полу-реалистичный поток: фон из случайных байт с регулярными E8/E9/Jcc + смещения.
    byte[] input = new byte[length];
    uint x = seed;

    for (int i = 0; i < length; i++)
    {
      x ^= x << 13;
      x ^= x >> 17;
      x ^= x << 5;
      input[i] = (byte)x;
    }

    // Втыкаем настоящие ветвления с короткими смещениями (конвертируемые).
    for (int i = 16; i + 8 < length; i += 37)
    {
      input[i] = (i % 3 == 0) ? (byte)0xE8 : (byte)0xE9;
      int rel = (i * 7) % 1024;
      input[i + 1] = (byte)rel;
      input[i + 2] = (byte)(rel >> 8);
      input[i + 3] = 0x00;
      input[i + 4] = 0x00;
    }

    AssertRoundTrip(input);
  }
}
