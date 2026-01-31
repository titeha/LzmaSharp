using System.Buffers.Binary;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSignatureHeaderTests
{
  [Fact]
  public void TryRead_Ok_ПарситПоля_ИПроверяетCRC()
  {
    const byte versionMajor = 0;
    const byte versionMinor = 4;

    const ulong nextHeaderOffset = 123;
    const ulong nextHeaderSize = 456;
    const uint nextHeaderCrc = 0x11223344;

    byte[] input = new byte[SevenZipSignatureHeader.Size];

    // signature "7z\xBC\xAF\x27\x1C"
    input[0] = 0x37;
    input[1] = 0x7A;
    input[2] = 0xBC;
    input[3] = 0xAF;
    input[4] = 0x27;
    input[5] = 0x1C;

    // version
    input[6] = versionMajor;
    input[7] = versionMinor;

    // StartHeaderCRC (8..11) заполним после того, как положим StartHeader.

    BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(12, 8), nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(20, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(28, 4), nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(input.AsSpan(12, 20));
    BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8, 4), startHeaderCrc);

    var result = SevenZipSignatureHeader.TryRead(input, out var header, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.Ok, result);
    Assert.Equal(SevenZipSignatureHeader.Size, consumed);

    Assert.Equal(versionMajor, header.VersionMajor);
    Assert.Equal(versionMinor, header.VersionMinor);
    Assert.Equal(startHeaderCrc, header.StartHeaderCrc);
    Assert.Equal(nextHeaderOffset, header.NextHeaderOffset);
    Assert.Equal(nextHeaderSize, header.NextHeaderSize);
    Assert.Equal(nextHeaderCrc, header.NextHeaderCrc);
  }

  [Fact]
  public void TryRead_NeedMoreInput_ЕслиНеХватаетДанных()
  {
    // меньше, чем сигнатура
    byte[] tooSmall1 = [0x37, 0x7A, 0xBC, 0xAF, 0x27];

    var result1 = SevenZipSignatureHeader.TryRead(tooSmall1, out _, out int consumed1);
    Assert.Equal(SevenZipSignatureHeaderReadResult.NeedMoreInput, result1);
    Assert.Equal(0, consumed1);

    // сигнатура есть, но не хватает до 32 байт
    byte[] tooSmall2 = new byte[SevenZipSignatureHeader.Size - 1];
    tooSmall2[0] = 0x37;
    tooSmall2[1] = 0x7A;
    tooSmall2[2] = 0xBC;
    tooSmall2[3] = 0xAF;
    tooSmall2[4] = 0x27;
    tooSmall2[5] = 0x1C;

    var result2 = SevenZipSignatureHeader.TryRead(tooSmall2, out _, out int consumed2);
    Assert.Equal(SevenZipSignatureHeaderReadResult.NeedMoreInput, result2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиСигнатураНеСовпадает()
  {
    byte[] input = new byte[SevenZipSignatureHeader.Size];
    input[0] = 0x00;
    input[1] = 0x7A;
    input[2] = 0xBC;
    input[3] = 0xAF;
    input[4] = 0x27;
    input[5] = 0x1C;

    var result = SevenZipSignatureHeader.TryRead(input, out _, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.InvalidData, result);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиCRCНеСовпадает()
  {
    byte[] input = new byte[SevenZipSignatureHeader.Size];

    // signature "7z\xBC\xAF\x27\x1C"
    input[0] = 0x37;
    input[1] = 0x7A;
    input[2] = 0xBC;
    input[3] = 0xAF;
    input[4] = 0x27;
    input[5] = 0x1C;

    input[6] = 0;
    input[7] = 4;

    // заполняем StartHeader
    BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(12, 8), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(20, 8), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(28, 4), 0xAABBCCDD);

    // Пишем заведомо неверную CRC.
    BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8, 4), 0xDEADBEEF);

    var result = SevenZipSignatureHeader.TryRead(input, out _, out int consumed);

    Assert.Equal(SevenZipSignatureHeaderReadResult.InvalidData, result);
    Assert.Equal(0, consumed);
  }
}
