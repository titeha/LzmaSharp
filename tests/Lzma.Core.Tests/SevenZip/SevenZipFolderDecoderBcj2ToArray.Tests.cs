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
}
