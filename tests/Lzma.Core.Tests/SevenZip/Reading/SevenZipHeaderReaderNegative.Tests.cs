using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipHeaderReaderNegativeTests
{
  [Fact]
  public void TryRead_InvalidData_ЕслиArchivePropertiesПовторяется()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.ArchiveProperties,
      SevenZipNid.End,
      SevenZipNid.ArchiveProperties,
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиРазмерArchivePropertyБольшеIntMaxValue()
  {
    byte[] data = Concat(
      [
        SevenZipNid.Header,
        SevenZipNid.ArchiveProperties,
        0x19, // произвольный PropertyType
      ],
      EncodeUInt64LongForm((ulong)int.MaxValue + 1UL));

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиДанныеArchivePropertyОбрезаны()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.ArchiveProperties,
      0x19, // произвольный PropertyType
      0x02, // size = 2
      0xAA, // есть только один байт данных
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиAdditionalStreamsInfoПовторяется()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.AdditionalStreamsInfo,
      SevenZipNid.End,
      SevenZipNid.AdditionalStreamsInfo,
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиMainStreamsInfoПовторяется()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.End,
      SevenZipNid.MainStreamsInfo,
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиFilesInfoПовторяется()
  {
    byte[] data = Concat(
      [SevenZipNid.Header],
      CreateMinimalFilesInfo(),
      CreateMinimalFilesInfo());

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиMainStreamsInfoСодержитНекорректныйStreamsInfo()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.UnpackInfo,
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиAdditionalStreamsInfoСодержитНеподдерживаемыйStreamsInfo()
  {
    byte[] data = Concat(
      [
        SevenZipNid.Header,
        SevenZipNid.AdditionalStreamsInfo,
        SevenZipNid.PackInfo,
        0x00, // packPos = 0
      ],
      EncodeUInt64LongForm((ulong)int.MaxValue + 1UL));

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_NotSupported_ЕслиВстреченНеизвестныйРаздел()
  {
    byte[] data =
    [
      SevenZipNid.Header,
      0x18,
    ];

    var res = SevenZipHeaderReader.TryRead(data, out _, out int bytesConsumed);

    Assert.Equal(SevenZipHeaderReadResult.NotSupported, res);
    Assert.Equal(0, bytesConsumed);
  }

  private static byte[] CreateMinimalFilesInfo()
  {
    return
    [
      SevenZipNid.FilesInfo,
      0x00, // NumFiles = 0
      SevenZipNid.End,
    ];
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
