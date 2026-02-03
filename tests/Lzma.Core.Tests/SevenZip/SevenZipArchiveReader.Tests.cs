using System.Buffers.Binary;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipArchiveReaderTests
{
  [Fact]
  public void Read_ПустойАрхив_ВозвращаетOk()
  {
    // NextHeader = Header + End
    byte[] nextHeaderBytes =
    [
      SevenZipNid.Header,
      SevenZipNid.End,
    ];

    byte[] file = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    const ulong nextHeaderOffset = 0;
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);

    WriteSignatureHeader(file, nextHeaderOffset, (ulong)nextHeaderBytes.Length, nextHeaderCrc);

    nextHeaderBytes.CopyTo(file.AsSpan(SevenZipSignatureHeader.Size));

    var reader = new SevenZipArchiveReader();
    var res = reader.Read(file, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, res);
    Assert.Equal(file.Length, bytesConsumed);

    var expected = new SevenZipSignatureHeader(nextHeaderOffset, (ulong)nextHeaderBytes.Length, nextHeaderCrc);
    Assert.Equal(expected, reader.SignatureHeader);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);

    // Заголовок (Header) пустой => StreamsInfo нет, FilesInfo пустой.
    Assert.Null(reader.Header.StreamsInfo);
    Assert.Equal(0UL, reader.Header.FilesInfo.FileCount);
  }

  [Fact]
  public void Read_Потоково_ПустойАрхив_Работает()
  {
    byte[] nextHeaderBytes =
    [
      SevenZipNid.Header,
      SevenZipNid.End,
    ];

    byte[] file = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    const ulong nextHeaderOffset = 0;
    uint nextHeaderCrc = Crc32.Compute(nextHeaderBytes);

    WriteSignatureHeader(file, nextHeaderOffset, (ulong)nextHeaderBytes.Length, nextHeaderCrc);

    nextHeaderBytes.CopyTo(file.AsSpan(SevenZipSignatureHeader.Size));

    var reader = new SevenZipArchiveReader();

    // Подаём по кускам.
    int bytesConsumedTotal = 0;
    const int chunkSize = 3;

    while (bytesConsumedTotal < file.Length)
    {
      int remaining = file.Length - bytesConsumedTotal;
      int take = Math.Min(remaining, chunkSize);

      var res = reader.Read(file.AsSpan(bytesConsumedTotal, take), out int consumed);
      bytesConsumedTotal += consumed;

      if (res == SevenZipArchiveReadResult.Ok)
        break;

      Assert.Equal(SevenZipArchiveReadResult.NeedMoreInput, res);
      Assert.True(consumed > 0);
    }

    Assert.Equal(file.Length, bytesConsumedTotal);
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Result);

    Assert.Null(reader.Header.StreamsInfo);
    Assert.Equal(0UL, reader.Header.FilesInfo.FileCount);
  }

  [Fact]
  public void Read_НеверныйCRC_ВозвращаетInvalidData()
  {
    byte[] nextHeaderBytes =
    [
      SevenZipNid.Header,
      SevenZipNid.End,
    ];

    byte[] file = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    const ulong nextHeaderOffset = 0;
    const uint nextHeaderCrc = 123; // специально неверно

    WriteSignatureHeader(file, nextHeaderOffset, (ulong)nextHeaderBytes.Length, nextHeaderCrc);

    nextHeaderBytes.CopyTo(file.AsSpan(SevenZipSignatureHeader.Size));

    var reader = new SevenZipArchiveReader();
    var res = reader.Read(file, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res);
    Assert.Equal(file.Length, bytesConsumed);
  }

  private static void WriteSignatureHeader(Span<byte> file, ulong nextHeaderOffset, ulong nextHeaderSize, uint nextHeaderCrc)
  {
    SevenZipSignatureHeader.Signature.CopyTo(file);

    file[6] = SevenZipSignatureHeader.MajorVersion;
    file[7] = SevenZipSignatureHeader.MinorVersion;

    // StartHeader (стартовый заголовок) = NextHeaderOffset (8) + NextHeaderSize (8) + NextHeaderCRC (4)
    Span<byte> startHeader = stackalloc byte[20];
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader[..8], nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(8, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(startHeader.Slice(16, 4), nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(startHeader);
    BinaryPrimitives.WriteUInt32LittleEndian(file.Slice(8, 4), startHeaderCrc);

    // Байты StartHeader
    startHeader.CopyTo(file.Slice(12, 20));
  }
}
