using System.Buffers.Binary;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSignatureHeaderTests
{
  [Fact]
  public void TryRead_NeedMoreInput_ЕслиМеньше6Байт()
  {
    byte[] input = [0x37, 0x7A, 0xBC, 0xAF, 0x27];

    var res = SevenZipSignatureHeader.TryRead(input, out var header, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Equal(default, header);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиСигнатураНеСовпадает()
  {
    // Первые 6 байт не совпадают с 37 7A BC AF 27 1C.
    byte[] input = [0x00, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];

    var res = SevenZipSignatureHeader.TryRead(input, out _, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.InvalidData, res);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиЗаголовокОбрезан()
  {
    // Сигнатура есть, но до 32 байт не дотягиваем.
    byte[] input =
    [
      0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, // signature
      0x00, 0x04, // version
      0x01, 0x02, 0x03, 0x04, // start header crc
      // дальше байт нет
    ];

    var res = SevenZipSignatureHeader.TryRead(input, out _, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_Ok_ПарситПоляКакОжидается()
  {
    byte[] bytes = new byte[SevenZipSignatureHeader.Size];

    // signature
    bytes[0] = 0x37;
    bytes[1] = 0x7A;
    bytes[2] = 0xBC;
    bytes[3] = 0xAF;
    bytes[4] = 0x27;
    bytes[5] = 0x1C;

    // version
    bytes[6] = 0x00; // major
    bytes[7] = 0x04; // minor

    // start header crc
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 0xA1B2C3D4);

    // start header
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12, 8), 0x0102030405060708UL); // NextHeaderOffset
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(20, 8), 0x0A0B0C0D0E0F1011UL); // NextHeaderSize
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28, 4), 0x11223344); // NextHeaderCrc

    var res = SevenZipSignatureHeader.TryRead(bytes, out var header, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.Ok, res);
    Assert.Equal(SevenZipSignatureHeader.Size, consumed);

    Assert.Equal((byte)0x00, header.VersionMajor);
    Assert.Equal((byte)0x04, header.VersionMinor);
    Assert.Equal(0xA1B2C3D4u, header.StartHeaderCrc);
    Assert.Equal(0x0102030405060708UL, header.NextHeaderOffset);
    Assert.Equal(0x0A0B0C0D0E0F1011UL, header.NextHeaderSize);
    Assert.Equal(0x11223344u, header.NextHeaderCrc);

    Assert.Equal(SevenZipSignatureHeader.Size + header.NextHeaderOffset, header.NextHeaderAbsoluteOffset);
  }
}
