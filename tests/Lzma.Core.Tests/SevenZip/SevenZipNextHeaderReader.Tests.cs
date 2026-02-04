using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipNextHeaderReaderTests
{
  [Fact]
  public void Read_OneShot_ЧитаетNextHeader_ИПроверяетCrc()
  {
    byte[] nextHeader = [1, 2, 3, 4, 5, 6];
    byte[] file = Build7zFile(nextHeaderOffset: 3, nextHeader, out var expectedSigHeader);

    var reader = new SevenZipNextHeaderReader();

    var res = reader.Read(file, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.Ok, res);
    Assert.Equal(file.Length, consumed);
    Assert.True(reader.HasSignatureHeader);
    Assert.Equal(expectedSigHeader.NextHeaderOffset, reader.SignatureHeader.NextHeaderOffset);
    Assert.Equal(expectedSigHeader.NextHeaderSize, reader.SignatureHeader.NextHeaderSize);
    Assert.Equal(expectedSigHeader.NextHeaderCrc, reader.SignatureHeader.NextHeaderCrc);
    Assert.Equal(nextHeader, reader.NextHeader.ToArray());

    Assert.Equal(
      file.AsSpan(SevenZipSignatureHeader.Size, (int)expectedSigHeader.NextHeaderOffset).ToArray(),
      reader.PackedStreams.ToArray());
  }

  [Fact]
  public void Read_ПотоковоКрошечнымиКусками_Работает()
  {
    byte[] nextHeader = [10, 11, 12];
    byte[] file = Build7zFile(nextHeaderOffset: 0, nextHeader, out var expectedSigHeader);

    var reader = new SevenZipNextHeaderReader();

    int pos = 0;
    while (true)
    {
      int chunkSize = Math.Min(2, file.Length - pos);
      ReadOnlySpan<byte> chunk = file.AsSpan(pos, chunkSize);

      var res = reader.Read(chunk, out int consumed);
      Assert.True(consumed > 0, "Декодер не продвинулся: не потребил ввод.");

      pos += consumed;

      if (res == SevenZipNextHeaderReadResult.Ok)
        break;

      Assert.Equal(SevenZipNextHeaderReadResult.NeedMoreInput, res);
      Assert.True(pos < file.Length, "Вход закончился раньше, чем декодер завершил чтение NextHeader.");
    }

    Assert.Equal(file.Length, pos);
    Assert.Equal(nextHeader, reader.NextHeader.ToArray());

    Assert.Equal(
      file.AsSpan(SevenZipSignatureHeader.Size, (int)expectedSigHeader.NextHeaderOffset).ToArray(),
      reader.PackedStreams.ToArray());
  }

  [Fact]
  public void Read_ВозвращаетInvalidData_ЕслиCrcНеСходится()
  {
    byte[] nextHeader = [1, 2, 3, 4];
    byte[] file = Build7zFile(nextHeaderOffset: 1, nextHeader, out _);

    // Портим один байт в самом NextHeader (CRC в header остаётся прежним).
    file[^1] ^= 0xFF;

    var reader = new SevenZipNextHeaderReader();
    var res = reader.Read(file, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.InvalidData, res);
    Assert.Equal(file.Length, consumed);
  }

  [Fact]
  public void Read_ВозвращаетNeedMoreInput_ЕслиNextHeaderНеДочитан()
  {
    byte[] nextHeader = [1, 2, 3, 4, 5];
    byte[] file = Build7zFile(nextHeaderOffset: 0, nextHeader, out var expectedSigHeader);

    // Обрезаем файл: не хватает одного байта NextHeader.
    byte[] truncated = [.. file.Take(file.Length - 1)];

    var reader = new SevenZipNextHeaderReader();
    var res = reader.Read(truncated, out int consumed);

    Assert.Equal(SevenZipNextHeaderReadResult.NeedMoreInput, res);
    Assert.Equal(truncated.Length, consumed);
  }

  private static byte[] Build7zFile(ulong nextHeaderOffset, byte[] nextHeader, out SevenZipSignatureHeader signatureHeader)
  {
    if (nextHeader is null)
      throw new ArgumentNullException(nameof(nextHeader));

    // Поля StartHeader.
    ulong nextHeaderSize = (ulong)nextHeader.Length;
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // Собираем StartHeader (20 байт).
    Span<byte> startHeader = stackalloc byte[20];
    WriteUInt64LE(startHeader.Slice(0, 8), nextHeaderOffset);
    WriteUInt64LE(startHeader.Slice(8, 8), nextHeaderSize);
    WriteUInt32LE(startHeader.Slice(16, 4), nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(startHeader);

    // Собираем SignatureHeader (32 байта).
    byte[] header = new byte[SevenZipSignatureHeader.Size];

    // Signature: 37 7A BC AF 27 1C
    header[0] = 0x37;
    header[1] = 0x7A;
    header[2] = 0xBC;
    header[3] = 0xAF;
    header[4] = 0x27;
    header[5] = 0x1C;

    // Version: 0.4
    header[6] = 0;
    header[7] = 4;

    // StartHeaderCRC.
    WriteUInt32LE(header.AsSpan(8, 4), startHeaderCrc);

    // StartHeader.
    startHeader.CopyTo(header.AsSpan(12, 20));

    // Проверим, что наш builder собрал корректный header.
    var readRes = SevenZipSignatureHeader.TryRead(header, out signatureHeader, out _);
    Assert.Equal(SevenZipSignatureHeaderReadResult.Done, readRes);

    byte[] file = new byte[header.Length + (int)nextHeaderOffset + nextHeader.Length];
    header.CopyTo(file, 0);

    // Промежуточные данные между SignatureHeader и NextHeader.
    for (int i = 0; i < (int)nextHeaderOffset; i++)
      file[header.Length + i] = 0xCC;

    nextHeader.CopyTo(file, header.Length + (int)nextHeaderOffset);
    return file;
  }

  private static void WriteUInt32LE(Span<byte> dst, uint value)
  {
    dst[0] = (byte)(value);
    dst[1] = (byte)(value >> 8);
    dst[2] = (byte)(value >> 16);
    dst[3] = (byte)(value >> 24);
  }

  private static void WriteUInt64LE(Span<byte> dst, ulong value)
  {
    dst[0] = (byte)(value);
    dst[1] = (byte)(value >> 8);
    dst[2] = (byte)(value >> 16);
    dst[3] = (byte)(value >> 24);
    dst[4] = (byte)(value >> 32);
    dst[5] = (byte)(value >> 40);
    dst[6] = (byte)(value >> 48);
    dst[7] = (byte)(value >> 56);
  }
}
