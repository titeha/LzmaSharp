using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderEncodedHeaderTests
{
  [Fact]
  public void Read_EncodedNextHeader_Lzma2Lzma_WithNonZeroPackPos_Ok_AndFileDecodes()
  {
    // Небольшой, но не тривиальный вход.
    byte[] plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    byte[] archive = Build7zArchive_SingleFile_EncodedHeader_Lzma2Lzma(
        plainFileBytes: plain,
        fileName: "file.bin",
        fileDictionarySize: 1 << 20,
        headerDictionarySize: 1 << 20,
        headerMaxUnpackChunkSize: 64);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult readResult = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, readResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    Assert.Equal(1UL, header.FilesInfo.FileCount);
    Assert.True(header.FilesInfo.HasNames);
    Assert.Equal("file.bin", header.FilesInfo.Names![0]);

    // Важно: PackedStreams содержит и данные файла, и packed stream EncodedHeader.
    // Декодер folder'а обязан вырезать нужный кусок по PackInfo/PackPos/PackSizes.
    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: header.StreamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, folderBytes);
  }

  [Fact]
  public void Read_EncodedHeader_Lzma2Lzma_PackPosNonZero_ДекодируетИЧитаетЗаголовок()
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
    Assert.True(Lzma2Properties.TryEncode(dictionarySize, out byte lzma2PropsByte));

    // Сжимаем header в LZMA2, но именно LZMA-чанком (не COPY).
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] packedHeader = Lzma2LzmaEncoder.EncodeLiteralOnly(
        data: plainHeader,
        lzmaProperties: lzmaProps,
        dictionarySize: dictionarySize,
        out _);

    // Делаем “мусорный” префикс в PackedStreams, чтобы PackPos был ненулевым.
    byte[] prefix = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    byte[] nextHeader = BuildNextHeader_EncodedHeader(
        packPos: prefix.Length,
        packedHeaderSize: packedHeader.Length,
        unpackedHeaderSize: plainHeader.Length,
        lzma2PropertiesByte: lzma2PropsByte);

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
    SevenZipArchiveReadResult res = reader.Read(archiveBytes, out _);

    Assert.Equal(SevenZipArchiveReadResult.Ok, res);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
    Assert.Equal(plainHeader, reader.DecodedHeaderBytes);

    Assert.True(reader.Header.HasValue);
    Assert.Equal(0UL, reader.Header.Value.FilesInfo.FileCount);

    byte[] expectedPackedStreams = new byte[prefix.Length + packedHeader.Length];
    Buffer.BlockCopy(prefix, 0, expectedPackedStreams, 0, prefix.Length);
    Buffer.BlockCopy(packedHeader, 0, expectedPackedStreams, prefix.Length, packedHeader.Length);

    Assert.Equal(expectedPackedStreams, reader.PackedStreams);
  }

  private static byte[] BuildNextHeader_EncodedHeader(int packPos, int packedHeaderSize, int unpackedHeaderSize, byte lzma2PropertiesByte)
  {
    byte[] buf = new byte[256];
    int o = 0;

    buf[o++] = SevenZipNid.EncodedHeader;

    // StreamsInfo
    buf[o++] = SevenZipNid.PackInfo;

    // PackPos
    SevenZipEncodedUInt64.TryWrite((ulong)packPos, buf.AsSpan(o), out int w);
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

    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // NumFolders = 1
    o += w;

    buf[o++] = 0x00; // External = 0

    // Folder
    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // NumCoders = 1
    o += w;

    buf[o++] = 0x21; // Coder main byte
    buf[o++] = 0x21; // MethodId (LZMA2)

    SevenZipEncodedUInt64.TryWrite(1, buf.AsSpan(o), out w); // Properties size = 1
    o += w;

    buf[o++] = lzma2PropertiesByte;

    buf[o++] = SevenZipNid.CodersUnpackSize;
    SevenZipEncodedUInt64.TryWrite((ulong)unpackedHeaderSize, buf.AsSpan(o), out w);
    o += w;

    buf[o++] = SevenZipNid.End; // end UnpackInfo
    buf[o++] = SevenZipNid.End; // end StreamsInfo
    buf[o++] = SevenZipNid.End; // end EncodedHeader

    return buf.AsSpan(0, o).ToArray();
  }

  private static byte[] Build7zArchive_SingleFile_EncodedHeader_Lzma2Lzma(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      int fileDictionarySize,
      int headerDictionarySize,
      int headerMaxUnpackChunkSize)
  {
    // 1) Packed stream данных файла (COPY — чтобы изолировать тест на EncodedHeader).
    byte[] filePackedStream = Lzma2CopyEncoder.Encode(plainFileBytes, fileDictionarySize, out byte fileLzma2Prop);

    // 2) Inner header (обычный Header), который описывает filePackedStream.
    byte[] innerHeader = BuildInnerHeader_SingleFile_SingleFolder_Lzma2(
        packSize: (ulong)filePackedStream.Length,
        unpackSize: (ulong)plainFileBytes.Length,
        fileName: fileName,
        lzma2PropertiesByte: fileLzma2Prop);

    // 3) Packed stream для EncodedHeader: LZMA2 с LZMA-чанками.
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] encodedHeaderPackedStream = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
        data: innerHeader,
        lzmaProperties: lzmaProps,
        dictionarySize: headerDictionarySize,
        maxUnpackChunkSize: headerMaxUnpackChunkSize,
        out _);

    // Properties байт (dictionary prop) для LZMA2 в 7z.
    _ = Lzma2CopyEncoder.Encode([], headerDictionarySize, out byte headerLzma2Prop);

    // 4) Outer NextHeader = EncodedHeader (StreamsInfo), который описывает encodedHeaderPackedStream.
    //    Важно: packPos != 0 (после данных файла).
    ulong headerPackPos = (ulong)filePackedStream.Length;

    byte[] outerNextHeader = BuildOuterNextHeader_EncodedHeader_Lzma2(
        packPos: headerPackPos,
        packSize: (ulong)encodedHeaderPackedStream.Length,
        unpackSize: (ulong)innerHeader.Length,
        lzma2PropertiesByte: headerLzma2Prop);

    uint nextHeaderCrc = Crc32.Compute(outerNextHeader);

    ulong nextHeaderOffset = (ulong)filePackedStream.Length + (ulong)encodedHeaderPackedStream.Length;
    ulong nextHeaderSize = (ulong)outerNextHeader.Length;

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: nextHeaderOffset,
        NextHeaderSize: nextHeaderSize,
        NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    // Итоговый архив: [SignatureHeader][filePackedStream][encodedHeaderPackedStream][outerNextHeader]
    var archive = new byte[sigBytes.Length + filePackedStream.Length + encodedHeaderPackedStream.Length + outerNextHeader.Length];
    sigBytes.CopyTo(archive, 0);

    filePackedStream.CopyTo(archive.AsSpan(sigBytes.Length));
    encodedHeaderPackedStream.CopyTo(archive.AsSpan(sigBytes.Length + filePackedStream.Length));
    outerNextHeader.CopyTo(archive.AsSpan(sigBytes.Length + filePackedStream.Length + encodedHeaderPackedStream.Length));

    return archive;
  }

  private static byte[] BuildInnerHeader_SingleFile_SingleFolder_Lzma2(
      ulong packSize,
      ulong unpackSize,
      string fileName,
      byte lzma2PropertiesByte)
  {
    List<byte> headerPayload =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
            SevenZipNid.PackInfo,
        ];

    // PackPos (внутри data area). Здесь данные файла начинаются с 0.
    WriteU64(headerPayload, 0);

    // NumPackStreams
    WriteU64(headerPayload, 1);

    // Size
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, packSize);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);

    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0);       // External = 0

    // NumCoders
    WriteU64(headerPayload, 1);

    // Coder: LZMA2 (0x21) + properties size 1.
    headerPayload.Add(0b0010_0001); // hasProps=1, idSize=1, simple coder (1 in / 1 out)
    headerPayload.Add(0x21);
    headerPayload.Add(1);
    headerPayload.Add(lzma2PropertiesByte);

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, unpackSize);

    headerPayload.Add(SevenZipNid.End); // End UnpackInfo

    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1); // NumFiles

    headerPayload.Add(SevenZipNid.Name);
    var nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(headerPayload, (ulong)(1 + nameBytes.Length));
    headerPayload.Add(0); // External = 0
    headerPayload.AddRange(nameBytes);

    headerPayload.Add(SevenZipNid.End); // End FilesInfo
    headerPayload.Add(SevenZipNid.End); // End Header

    return [.. headerPayload];
  }

  private static byte[] BuildOuterNextHeader_EncodedHeader_Lzma2(
      ulong packPos,
      ulong packSize,
      ulong unpackSize,
      byte lzma2PropertiesByte)
  {
    List<byte> h =
    [
        SevenZipNid.EncodedHeader,

            // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
            SevenZipNid.PackInfo,
        ];

    WriteU64(h, packPos);
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, packSize);
    h.Add(SevenZipNid.End); // End PackInfo

    h.Add(SevenZipNid.UnpackInfo);

    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0);       // External = 0

    // NumCoders
    WriteU64(h, 1);

    // Coder: LZMA2 (0x21) + properties size 1.
    h.Add(0b0010_0001);
    h.Add(0x21);
    h.Add(1);
    h.Add(lzma2PropertiesByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo

    h.Add(SevenZipNid.SubStreamsInfo);
    h.Add(SevenZipNid.NumUnpackStream);
    WriteU64(h, 1);
    h.Add(SevenZipNid.End); // End SubStreamsInfo

    h.Add(SevenZipNid.End); // End StreamsInfo

    return [.. h];
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
