using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipPackInfoReaderNegativeTests
{
  [Fact]
  public void TryRead_InvalidData_ЕслиОтсутствуетРазделSize()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.End,
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиCrcИдетДоSize()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Crc,
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиРазделSizeПовторяется()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x01,
      SevenZipNid.Size,
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиРазделCrcПовторяется()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x01,
      SevenZipNid.Crc,
      0x01, // AllAreDefined = 1
      0x44, 0x33, 0x22, 0x11,
      SevenZipNid.Crc,
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиNumPackStreamsБольшеIntMaxValue()
  {
    byte[] data = Concat(
      [SevenZipNid.PackInfo, 0x00],
      EncodeUInt64LongForm((ulong)int.MaxValue + 1UL));

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиНеХватаетБайтовCrcКогдаAllAreDefined()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x01,
      SevenZipNid.Crc,
      0x01, // AllAreDefined = 1
      0x44, 0x33, 0x22, // не хватает одного байта CRC
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиНеХватаетBitsetДляPartialCrc()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x02, // numPackStreams = 2
      SevenZipNid.Size,
      0x01,
      0x02,
      SevenZipNid.Crc,
      0x00, // AllAreDefined = 0
      // дальше должен идти битовый вектор Defined[2]
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиНеХватаетЗначенияCrcДляPartialCrc()
  {
    byte[] data =
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x02, // numPackStreams = 2
      SevenZipNid.Size,
      0x01,
      0x02,
      SevenZipNid.Crc,
      0x00, // AllAreDefined = 0
      0x40, // Defined = [false, true]
      0x44, 0x33, 0x22, // не хватает одного байта CRC
    ];

    var res = SevenZipPackInfoReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipPackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }

  private static byte[] EncodeUInt64LongForm(ulong value)
  {
    byte[] data = new byte[9];
    data[0] = 0xFF;

    ulong v = value;
    for (int i = 0; i < 8; i++)
    {
      data[1 + i] = (byte)(v & 0xFF);
      v >>= 8;
    }

    return data;
  }

  private static byte[] Concat(params byte[][] parts)
  {
    int totalLength = 0;
    for (int i = 0; i < parts.Length; i++)
      totalLength += parts[i].Length;

    byte[] result = new byte[totalLength];
    int offset = 0;
    for (int i = 0; i < parts.Length; i++)
    {
      Buffer.BlockCopy(parts[i], 0, result, offset, parts[i].Length);
      offset += parts[i].Length;
    }

    return result;
  }
}
