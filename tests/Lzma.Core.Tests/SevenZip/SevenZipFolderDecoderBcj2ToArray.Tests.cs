using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderBcj2ToArrayTests
{
  [Fact]
  public void TryDecodeBcj2ToArray_RangeStreamКорочеПятиБайт_ВозвращаетInvalidData()
  {
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: [0x10],
        buf1: [],
        buf2: [],
        buf3: [0x00, 0x01, 0x02, 0x03],
        outSize: 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_БезМаркеровПерехода_КопируетBuf0КакЕсть()
  {
    byte[] buf0 = [0x10, 0x20, 0x30, 0x40, 0x50];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [],
        buf2: [],
        buf3: [0x00, 0x00, 0x00, 0x00, 0x00],
        outSize: buf0.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(buf0, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_БезМаркеровПереходаИПриНедостаткеДанных_ВозвращаетInvalidData()
  {
    byte[] buf0 = [0x10, 0x20, 0x30];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [],
        buf2: [],
        buf3: [0x00, 0x00, 0x00, 0x00, 0x00],
        outSize: buf0.Length + 1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Theory]
  [InlineData((byte)0xE8)]
  [InlineData((byte)0xE9)]
  public void TryDecodeBcj2ToArray_НетDisp32ВоВспомогательномПотоке_ВозвращаетInvalidData(byte opcode)
  {
    byte[] buf0 = [opcode];

    // Пять 0xFF дают максимально большой code после инициализации,
    // поэтому первая проверка вероятности уходит в ветку BIT=1
    // и декодер пытается взять disp32 из buf1/buf2.
    byte[] rangeStream = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [],
        buf2: [],
        buf3: rangeStream,
        outSize: 5,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_ОтрицательныйРазмерВыхода_ВозвращаетInvalidData()
  {
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: [0x10],
        buf1: [0x20],
        buf2: [0x30],
        buf3: [0x40],
        outSize: -1,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_НулевойРазмерВыхода_ВозвращаетOkСПустымМассивом()
  {
    // Намеренно даём buf3 короче 5 байт, чтобы проверить ранний выход
    // до инициализации range decoder.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: [0x10],
        buf1: [0x20],
        buf2: [0x30],
        buf3: [0x40],
        outSize: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Empty(output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_E8ПриBit0_ОставляетDisp32ВBuf0()
  {
    byte[] buf0 = [0xE8, 0x11, 0x22, 0x33, 0x44];

    // Пять нулей => code = 0 после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=0.
    // В этом режиме disp32 должен остаться в buf0,
    // а вспомогательные buf1/buf2 не должны использоваться.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0xAA, 0xBB, 0xCC, 0xDD],
        buf2: [0xEE, 0xFF, 0x00, 0x11],
        buf3: [0x00, 0x00, 0x00, 0x00, 0x00],
        outSize: buf0.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(buf0, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_JccПриBit0_ОставляетDisp32ВBuf0()
  {
    byte[] buf0 = [0x0F, 0x80, 0x11, 0x22, 0x33, 0x44];

    // Пять нулей => code = 0 после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=0.
    // Для Jcc это означает, что disp32 должен остаться в buf0,
    // а buf1/buf2 не должны использоваться.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0xAA, 0xBB, 0xCC, 0xDD],
        buf2: [0xEE, 0xFF, 0x00, 0x11],
        buf3: [0x00, 0x00, 0x00, 0x00, 0x00],
        outSize: buf0.Length,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(buf0, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_E9ПриBit1_БеретDisp32ИзBuf2ИПишетRel32()
  {
    byte[] buf0 = [0xE9];

    // Пять 0xFF дают code >= bound после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=1.
    //
    // Для E9 helper должен взять ABS disp32 из buf2, а не из buf1.
    // На момент пересчёта outPos уже равен 1, потому что opcode E9
    // уже записан в output. Поэтому:
    //
    //   rel32 = abs - (outPos + 4) = 9 - 5 = 4
    //
    // В output ждём E9 + little-endian rel32: 04 00 00 00.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0xAA, 0xBB, 0xCC, 0xDD],
        buf2: [0x00, 0x00, 0x00, 0x09],
        buf3: [0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        outSize: 5,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0xE9, 0x04, 0x00, 0x00, 0x00 }, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_JccПриBit1_БеретDisp32ИзBuf2ИПишетRel32()
  {
    byte[] buf0 = [0x0F, 0x80];

    // Пять 0xFF дают code >= bound после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=1.
    //
    // Для Jcc helper должен взять ABS disp32 из buf2.
    // На момент пересчёта outPos уже равен 2,
    // потому что opcode Jcc состоит из двух байт: 0F 80.
    //
    //   rel32 = abs - (outPos + 4) = 10 - 6 = 4
    //
    // В output ждём 0F 80 + little-endian rel32: 04 00 00 00.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0xAA, 0xBB, 0xCC, 0xDD],
        buf2: [0x00, 0x00, 0x00, 0x0A],
        buf3: [0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        outSize: 6,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0x0F, 0x80, 0x04, 0x00, 0x00, 0x00 }, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_E8ПриBit1_БеретDisp32ИзBuf1ИПишетRel32()
  {
    byte[] buf0 = [0xE8];

    // Пять 0xFF дают code >= bound после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=1.
    //
    // Для E8 helper должен взять ABS disp32 из buf1, а не из buf2.
    // На момент пересчёта outPos уже равен 1,
    // потому что opcode E8 уже записан в output.
    //
    //   rel32 = abs - (outPos + 4) = 9 - 5 = 4
    //
    // В output ждём E8 + little-endian rel32: 04 00 00 00.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0x00, 0x00, 0x00, 0x09],
        buf2: [0xAA, 0xBB, 0xCC, 0xDD],
        buf3: [0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        outSize: 5,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0xE8, 0x04, 0x00, 0x00, 0x00 }, output);
  }

  [Fact]
  public void TryDecodeBcj2ToArray_E8ПриBit1ИУсеченномRel32_ВозвращаетOkСУсеченнымВыходом()
  {
    byte[] buf0 = [0xE8];

    // Пять 0xFF дают code >= bound после инициализации range decoder,
    // поэтому первая вероятностная развилка идёт в BIT=1.
    //
    // Для E8 helper берёт ABS disp32 из buf1.
    // На момент пересчёта outPos уже равен 1:
    //
    //   rel32 = abs - (outPos + 4) = 9 - 5 = 4
    //
    // Полный результат был бы:
    //   E8 04 00 00 00
    //
    // Но outSize = 3, поэтому helper должен вернуть только:
    //   E8 04 00
    //
    // Это фиксирует текущий контракт: outSize контейнера ограничивает
    // итоговый output даже внутри четырёхбайтового rel32.
    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2ToArray(
        buf0: buf0,
        buf1: [0x00, 0x00, 0x00, 0x09],
        buf2: [0xAA, 0xBB, 0xCC, 0xDD],
        buf3: [0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
        outSize: 3,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(new byte[] { 0xE8, 0x04, 0x00 }, output);
  }
}
