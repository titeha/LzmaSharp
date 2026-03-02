using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderTests
{
  [Fact]
  public void TryRead_ИменаНесколькихФайлов_ДаетТочныеИмена()
  {
    // kFilesInfo
    //   numFiles = 3
    //   kName (external=0)
    //   kEnd

    byte[] namePayload = Encoding.Unicode.GetBytes("a\0b\0c\0");
    int namePropertySize = 1 + namePayload.Length;

    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x03, // numFiles = 3
      SevenZipNid.Name,
      (byte)namePropertySize, // size
      0x00, // external=0
      .. namePayload,
      SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);
    Assert.Equal((ulong)3, files.FileCount);

    Assert.NotNull(files.Names);
    Assert.Equal(new[] { "a", "b", "c" }, files.Names!);
  }

  [Fact]
  public void TryRead_НедостаточноВвода_ВозвращаетNeedMoreInput_ИНеПотребляетБайты()
  {
    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x01, // numFiles = 1
      SevenZipNid.Name,
      0x03, // size = 3
      0x00, // external=0
      0x61, // 'a' (НЕ ХВАТАЕТ второго байта и терминатора)
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NeedMoreInput, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ExternalИменаПокаНеПоддерживаются()
  {
    byte[] namePayload = Encoding.Unicode.GetBytes("a\0");
    int namePropertySize = 1 + namePayload.Length;

    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x01,

      SevenZipNid.Name,
      (byte)namePropertySize,
      0x01, // external=1 (не поддерживаем)
      .. namePayload,

      SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ЕслиИменМеньшеЧемФайлов_ЭтоInvalidData()
  {
    // numFiles = 2, но в данных только одно имя "a".
    byte[] namePayload = Encoding.Unicode.GetBytes("a\0");
    int namePropertySize = 1 + namePayload.Length;

    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x02,

      SevenZipNid.Name,
      (byte)namePropertySize,
      0x00,
      .. namePayload,

      SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_EmptyStreamVector_ЧитаетсяКорректно()
  {
    // numFiles = 3
    // EmptyStream: [false, true, false] => 0b0100_0000 => 0x40
    byte[] bytes =
    [
        SevenZipNid.FilesInfo,
        0x03,
        SevenZipNid.EmptyStream,
        0x01,   // size = 1
        0x40,   // bitfield
        SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);

    Assert.NotNull(files.EmptyStreams);
    Assert.Equal([false, true, false], files.EmptyStreams!);
  }

  [Fact]
  public void TryRead_EmptyStreamVector_НеверныйРазмер_ЭтоInvalidData()
  {
    // numFiles = 9 => нужен 2-байтовый bitfield, но заявлен/передан 1 байт.
    byte[] bytes =
    [
        SevenZipNid.FilesInfo,
        0x09,
        SevenZipNid.EmptyStream,
        0x01,   // size = 1 (ошибка)
        0xFF,
        SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_Crc_AllAreDefined_СохраняетCrcДляВсехФайлов()
  {
    // numFiles = 3
    // kCRC: allAreDefined=1 + 3 CRC32
    byte[] bytes =
    [
      SevenZipNid.FilesInfo, 0x03,

    SevenZipNid.Crc,
    0x0D, // size = 1 + 3*4
    0x01, // allAreDefined

    0x44, 0x33, 0x22, 0x11, // 0x11223344
    0x88, 0x77, 0x66, 0x55, // 0x55667788
    0xDD, 0xCC, 0xBB, 0xAA, // 0xAABBCCDD

    SevenZipNid.End,
  ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);

    Assert.NotNull(files.CrcDefined);
    Assert.NotNull(files.Crc);

    Assert.Equal([true, true, true], files.CrcDefined!);
    Assert.Equal([0x11223344u, 0x55667788u, 0xAABBCCDDu], files.Crc!);
  }

  [Fact]
  public void TryRead_Crc_PartialDefined_СохраняетCrcТолькоДляDefined()
  {
    // numFiles = 3
    // defined bits: [true,false,true] => 0x80 + 0x20 = 0xA0
    // CRCs идут в порядке индексов defined: file0, file2
    byte[] bytes =
    [
      SevenZipNid.FilesInfo, 0x03,

    SevenZipNid.Crc,
    0x0A, // size = 1(all) + 1(bits) + 2*4(crc)
    0x00, // allAreDefined=0
    0xA0, // bits

    0x44, 0x33, 0x22, 0x11, // file0 = 0x11223344
    0xDD, 0xCC, 0xBB, 0xAA, // file2 = 0xAABBCCDD

    SevenZipNid.End,
  ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);

    Assert.NotNull(files.CrcDefined);
    Assert.NotNull(files.Crc);

    Assert.Equal([true, false, true], files.CrcDefined!);
    Assert.Equal([0x11223344u, 0u, 0xAABBCCDDu], files.Crc!);
  }
}
