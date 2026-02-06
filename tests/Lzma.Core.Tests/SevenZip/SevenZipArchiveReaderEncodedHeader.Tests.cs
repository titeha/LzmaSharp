using System.Buffers.Binary;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipArchiveReaderEncodedHeaderTests
{
  [Fact]
  public void Read_EncodedHeader_ДекодируетИЧитаетЗаголовок()
  {
    // Минимальный «настоящий» Header (в распакованном виде):
    // Header -> MainStreamsInfo (пусто) -> FilesInfo (0 файлов) -> End.
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

    // Сжимаем header в LZMA2 (без LZMA, просто copy-чанк).
    const int dictionarySize = 4096;
    byte[] packedHeader = Lzma2CopyEncoder.EncodeChunkedAuto(plainHeader, dictionarySize, maxChunkPayloadSize: 32, out byte lzma2PropsByte);

    byte[] nextHeader = BuildNextHeader_EncodedHeader(
        packedHeaderSize: packedHeader.Length,
        unpackedHeaderSize: plainHeader.Length,
        lzma2PropertiesByte: lzma2PropsByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // 7z signature header указывает, что next header лежит сразу после packed streams.
    SevenZipSignatureHeader sig = new(
        nextHeaderOffset: (ulong)packedHeader.Length,
        nextHeaderSize: (ulong)nextHeader.Length,
        nextHeaderCrc: nextHeaderCrc);

    byte[] archiveBytes = new byte[SevenZipSignatureHeader.Size + packedHeader.Length + nextHeader.Length];

    WriteSignatureHeader(sig, archiveBytes);
    Buffer.BlockCopy(packedHeader, 0, archiveBytes, SevenZipSignatureHeader.Size, packedHeader.Length);
    Buffer.BlockCopy(nextHeader, 0, archiveBytes, SevenZipSignatureHeader.Size + packedHeader.Length, nextHeader.Length);

    SevenZipArchiveReader reader = new();
    SevenZipArchiveReadResult res = reader.Read(archiveBytes, out _);

    Assert.Equal(SevenZipArchiveReadResult.Ok, res);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);

    Assert.Equal(plainHeader, reader.DecodedHeaderBytes);

    Assert.True(reader.Header.HasValue);
    Assert.Equal(0UL, reader.Header.Value.FilesInfo.FileCount);

    Assert.Equal(packedHeader, reader.PackedStreams);
  }

  private static void WriteSignatureHeader(SevenZipSignatureHeader sig, Span<byte> dest)
  {
    if (dest.Length < SevenZipSignatureHeader.Size)
      throw new ArgumentException("Буфер слишком маленький", nameof(dest));

    // Signature
    SevenZipSignatureHeader.Signature.CopyTo(dest);

    // Version
    dest[6] = SevenZipSignatureHeader.MajorVersion;
    dest[7] = SevenZipSignatureHeader.MinorVersion;

    // StartHeader CRC
    BinaryPrimitives.WriteUInt32LittleEndian(dest[8..], sig.StartHeaderCrc);

    // Next header fields
    BinaryPrimitives.WriteUInt64LittleEndian(dest[12..], sig.NextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(dest[20..], sig.NextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(dest[28..], sig.NextHeaderCrc);
  }

  private static byte[] BuildNextHeader_EncodedHeader(int packedHeaderSize, int unpackedHeaderSize, byte lzma2PropertiesByte)
  {
    byte[] buf = new byte[256];
    int o = 0;

    buf[o++] = SevenZipNid.EncodedHeader;

    // StreamsInfo
    buf[o++] = SevenZipNid.PackInfo;

    // PackPos = 0
    SevenZipEncodedUInt64.TryWrite(0, buf.AsSpan(o), out int w);
    o += w;

    // NumPackStreams = 1
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.Size;

    SevenZipEncodedUInt64.TryWrite((ulong)packedHeaderSize, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.End; // end PackInfo

    // UnpackInfo
    buf[o++] = SevenZipNid.UnpackInfo;
    buf[o++] = SevenZipNid.Folder;

    // NumFolders = 1
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w);
    o += w;

    // External = 0
    buf[o++] = 0x00;

    // Folder
    // NumCoders = 1
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w);
    o += w;

    // Coder main byte:
    // 0x20 = hasAttributes
    // low 4 bits = codecIdSize (=1)
    buf[o++] = 0x21;

    // MethodId (LZMA2)
    buf[o++] = 0x21;

    // Properties size = 1
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = lzma2PropertiesByte;

    // CodersUnpackSize
    buf[o++] = SevenZipNid.CodersUnpackSize;
    SevenZipEncodedUInt64.TryWrite((ulong)unpackedHeaderSize, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.End; // end UnpackInfo

    buf[o++] = SevenZipNid.End; // end StreamsInfo
    buf[o++] = SevenZipNid.End; // end EncodedHeader

    return buf.AsSpan(0, o).ToArray();
  }
}
