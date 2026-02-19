using System.Buffers.Binary;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderEncodedHeaderLzma1Tests
{
  [Fact]
  public void Read_EncodedHeader_Lzma1_PackPosNonZero_ДекодируетИЧитаетЗаголовок()
  {
    byte[] plainHeader =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.End,

      SevenZipNid.FilesInfo,
      0x00, // fileCount = 0 (EncodedUInt64)
      SevenZipNid.End,

      SevenZipNid.End,
    ];

    const int dictionarySize = 4096;
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte lzmaPropsByte = lzmaProps.ToByteOrThrow();

    // Raw LZMA (7z method 03 01 01): без LZMA-Alone заголовка.
    byte[] packedHeader = new LzmaEncoder(lzmaProps, dictionarySize).EncodeLiteralOnly(plainHeader);

    // Делаем “мусорный” префикс в PackedStreams, чтобы PackPos был ненулевым.
    byte[] prefix = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    byte[] nextHeader = BuildNextHeader_EncodedHeader_Lzma1(
      packPos: prefix.Length,
      packedHeaderSize: packedHeader.Length,
      unpackedHeaderSize: plainHeader.Length,
      lzmaPropsByte: lzmaPropsByte,
      dictionarySize: dictionarySize);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    SevenZipSignatureHeader sig = new(
      NextHeaderOffset: (ulong)(prefix.Length + packedHeader.Length),
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archiveBytes = new byte[SevenZipSignatureHeader.Size + prefix.Length + packedHeader.Length + nextHeader.Length];
    sig.Write(archiveBytes);

    Buffer.BlockCopy(prefix, 0, archiveBytes, SevenZipSignatureHeader.Size, prefix.Length);
    Buffer.BlockCopy(packedHeader, 0, archiveBytes, SevenZipSignatureHeader.Size + prefix.Length, packedHeader.Length);
    Buffer.BlockCopy(nextHeader, 0, archiveBytes, SevenZipSignatureHeader.Size + prefix.Length + packedHeader.Length, nextHeader.Length);

    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult res = reader.Read(archiveBytes, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, res);
    Assert.Equal(archiveBytes.Length, bytesConsumed);

    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
    Assert.Equal(plainHeader, reader.DecodedHeaderBytes);

    Assert.True(reader.Header.HasValue);
    Assert.Equal(0UL, reader.Header.Value.FilesInfo.FileCount);
  }

  private static byte[] BuildNextHeader_EncodedHeader_Lzma1(
    int packPos,
    int packedHeaderSize,
    int unpackedHeaderSize,
    byte lzmaPropsByte,
    int dictionarySize)
  {
    byte[] buf = new byte[256];
    int o = 0;

    buf[o++] = SevenZipNid.EncodedHeader;

    // StreamsInfo
    buf[o++] = SevenZipNid.PackInfo;

    SevenZipEncodedUInt64.TryWrite((ulong)packPos, buf.AsSpan(o), out int w);
    o += w;

    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // NumPackStreams = 1
    o += w;

    buf[o++] = SevenZipNid.Size;
    SevenZipEncodedUInt64.TryWrite((ulong)packedHeaderSize, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.End; // End PackInfo

    // UnpackInfo
    buf[o++] = SevenZipNid.UnpackInfo;
    buf[o++] = SevenZipNid.Folder;

    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // NumFolders = 1
    o += w;

    buf[o++] = 0x00; // External = 0

    // Folder
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // NumCoders = 1
    o += w;

    // mainByte: hasProps=1 (0x20) + idSize=3
    buf[o++] = 0x23;

    // MethodID = 03 01 01 (LZMA)
    buf[o++] = 0x03;
    buf[o++] = 0x01;
    buf[o++] = 0x01;

    SevenZipEncodedUInt64.TryWrite(5, buf.AsSpan(o), out w); // props size = 5
    o += w;

    buf[o++] = lzmaPropsByte;
    BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(o, 4), (uint)dictionarySize);
    o += 4;

    buf[o++] = SevenZipNid.CodersUnpackSize;
    SevenZipEncodedUInt64.TryWrite((ulong)unpackedHeaderSize, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.End; // End UnpackInfo
    buf[o++] = SevenZipNid.End; // End StreamsInfo
    buf[o++] = SevenZipNid.End; // End EncodedHeader

    return buf.AsSpan(0, o).ToArray();
  }
}
