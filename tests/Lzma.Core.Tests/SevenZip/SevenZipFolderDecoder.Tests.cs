using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipFolderDecoderTests
{
  [Fact]
  public void DecodeFolder_SingleFolder_Lzma2Copy_Returns_OriginalBytes()
  {
    byte[] plain =
    [
        0x41, 0x42, 0x43, 0x44, 0x45,
            0x00,
            0xFF,
            0x10, 0x20, 0x30,
            0x41, 0x42, 0x43, 0x44, 0x45,
        ];

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(
        plainFileBytes: plain,
        fileName: "file.bin",
        dictionarySize: 1 << 20);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(1UL, filesInfo.FileCount);
    Assert.True(filesInfo.HasNames);
    Assert.Equal("file.bin", filesInfo.Names![0]);

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, folderBytes);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma2_InvalidDictionaryProp_ReturnsInvalidData()
  {
    // pack stream пустой — нам не важно: мы должны отвалиться ДО вызова LZMA2-декодера,
    // потому что properties битые (41 > 40).
    var packInfo = new SevenZipPackInfo(
        packPos: 0,
        packSizes: [0]);

    var coder = new SevenZipCoderInfo(
        methodId: [0x21],     // LZMA2
        properties: [41],     // invalid
        numInStreams: 1,
        numOutStreams: 1);

    var folder = new SevenZipFolder(
        Coders: [coder],
        BindPairs: [],
        PackedStreamIndices: [0],
        NumInStreams: 1,
        NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [[0]]);

    var streamsInfo = new SevenZipStreamsInfo(
        packInfo: packInfo,
        unpackInfo: unpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo,
        packedStreams: [],
        folderIndex: 0,
        out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolder_SingleFolder_Lzma2LzmaChunkedLiteralOnly_Returns_OriginalBytes()
  {
    // Важно: вход больше maxUnpackChunkSize, чтобы гарантировать несколько LZMA-чанков.
    var plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2LzmaChunkedLiteralOnly(
        plainFileBytes: plain,
        fileName: "file-lzma2.bin",
        dictionarySize: 1 << 20,
        maxUnpackChunkSize: 64);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.Header.HasValue);
    SevenZipHeader header = reader.Header.Value;

    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(1UL, filesInfo.FileCount);
    Assert.True(filesInfo.HasNames);
    Assert.Equal("file-lzma2.bin", filesInfo.Names![0]);

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    SevenZipFolderDecodeResult decodeResult = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        output: out byte[] folderBytes);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, decodeResult);
    Assert.Equal(plain, folderBytes);
  }

  [Fact]
  public void DecodeFolderToArray_Lzma1_LiteralOnly_Returns_OriginalBytes()
  {
    var plain = new byte[256];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    int dictionarySize = 1 << 20;

    // Raw LZMA stream (без LZMA-Alone header).
    byte[] packed = new LzmaEncoder(lzmaProps, dictionarySize).EncodeLiteralOnly(plain);

    byte[] coderProps = new byte[5];
    coderProps[0] = lzmaProps.ToByteOrThrow();
    BinaryPrimitives.WriteUInt32LittleEndian(coderProps.AsSpan(1, 4), (uint)dictionarySize);

    var packInfo = new SevenZipPackInfo(packPos: 0, packSizes: [(ulong)packed.Length]);

    var coder = new SevenZipCoderInfo(
      methodId: [0x03, 0x01, 0x01], // LZMA
      properties: coderProps,
      numInStreams: 1,
      numOutStreams: 1);

    var folder = new SevenZipFolder(
      Coders: [coder],
      BindPairs: [],
      PackedStreamIndices: [0],
      NumInStreams: 1,
      NumOutStreams: 1);

    var unpackInfo = new SevenZipUnpackInfo(
      folders: [folder],
      folderUnpackSizes: [[(ulong)plain.Length]]);

    var streamsInfo = new SevenZipStreamsInfo(
      packInfo: packInfo,
      unpackInfo: unpackInfo,
      subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
      streamsInfo,
      packedStreams: packed,
      folderIndex: 0,
      output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(plain, output);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2LzmaChunkedLiteralOnly(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      int dictionarySize,
      int maxUnpackChunkSize)
  {
    // Упакованные данные (LZMA2 stream с LZMA-чанками).
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] packedStream = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
        data: plainFileBytes,
        lzmaProperties: lzmaProps,
        dictionarySize: dictionarySize,
        maxUnpackChunkSize: maxUnpackChunkSize,
        out _);

    // Байт properties для кодера LZMA2 в 7z — это кодировка dictionary size.
    // Чтобы не дублировать маппинг dictionarySize -> propertiesByte в тесте,
    // берём его из Lzma2CopyEncoder (для пустого ввода).
    _ = Lzma2CopyEncoder.Encode([], dictionarySize, out byte lzma2PropertiesByte);

    return Build7zArchive_SingleFile_SingleFolder_Lzma2PackedStream(
        packedStream: packedStream,
        lzma2PropertiesByte: lzma2PropertiesByte,
        plainFileBytesLength: plainFileBytes.Length,
        fileName: fileName);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2PackedStream(
      ReadOnlySpan<byte> packedStream,
      byte lzma2PropertiesByte,
      int plainFileBytesLength,
      string fileName)
  {
    // ----- NextHeader ("Header") -----
    // 7z Header = [Header][MainStreamsInfo][FilesInfo][End]
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      // MainStreamsInfo
      SevenZipNid.MainStreamsInfo,
      // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
      SevenZipNid.PackInfo,
    ];
    // PackPos
    WriteU64(headerPayload, 0);
    // NumPackStreams
    WriteU64(headerPayload, 1);
    // Size
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedStream.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);
    // Folder
    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0); // External = 0

    // Folder описывает цепочку кодеров.
    // NumCoders
    WriteU64(headerPayload, 1);

    // Coder info:
    //   MainByte:
    //     - IDSize = 1 (младшие 4 бита: 1 => 1 байт; 0 считается ошибкой)
    //     - IsSimple = 1 (один входной и один выходной поток)
    //     - HasProperties = 1 (есть свойства кодера)
    //     - HasAttributes = 0
    //     - MoreAlternativeMethods = 0
    //
    // Важно: в 7z флаг "simple" кодируется как (MainByte & 0x10) == 0.
    headerPayload.Add(0b0010_0001); // MainByte: idSize=1 (младшие 4 бита), hasProps=1
                                    // MethodID (LZMA2 = 0x21)
    headerPayload.Add(0x21);
    // Properties
    headerPayload.Add(1); // properties size
    headerPayload.Add(lzma2PropertiesByte);

    // BindPairs и PackedStreamIndices можно не писать:
    //  - при одном выходном потоке NumBindPairs == 0,
    //  - при NumPackedStreams == 1 индекс подразумевается равным 0.

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytesLength);

    headerPayload.Add(SevenZipNid.End); // End of UnpackInfo

    // SubStreamsInfo
    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End of MainStreamsInfo

    // FilesInfo
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1); // NumFiles

    // Name property.
    headerPayload.Add(SevenZipNid.Name);

    // Данные свойства "Name" идут как:
    //   Size (u64)
    //   External (byte)
    //   UTF-16LE null-terminated strings
    // Здесь: один файл.
    var nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(headerPayload, (ulong)(1 + nameBytes.Length));
    headerPayload.Add(0); // External = 0
    headerPayload.AddRange(nameBytes);

    headerPayload.Add(SevenZipNid.End); // End of FilesInfo

    headerPayload.Add(SevenZipNid.End); // End of Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
    // next header сразу после signature header
    ulong nextHeaderOffset = (ulong)packedStream.Length;
    ulong nextHeaderSize = (ulong)nextHeader.Length;

    SevenZipSignatureHeader sig = new(
        NextHeaderOffset: nextHeaderOffset,
        NextHeaderSize: nextHeaderSize,
        NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    // ----- Archive = [SignatureHeader][PackedStreams][NextHeader] -----
    var archive = new byte[sigBytes.Length + packedStream.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedStream.CopyTo(archive.AsSpan(sigBytes.Length));
    nextHeader.CopyTo(archive, sigBytes.Length + packedStream.Length);

    return archive;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(
      ReadOnlySpan<byte> plainFileBytes,
      string fileName,
      int dictionarySize)
  {
    // Упакованные данные (LZMA2 stream с COPY-чанками).
    byte[] packedHeader = Lzma2CopyEncoder.Encode(plainFileBytes, dictionarySize, out byte lzma2PropertiesByte);

    // ----- NextHeader ("Header") -----
    // 7z Header = [Header][MainStreamsInfo][FilesInfo][End]
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      // MainStreamsInfo
      SevenZipNid.MainStreamsInfo,
      // StreamsInfo = [PackInfo][UnpackInfo][SubStreamsInfo][End]
      SevenZipNid.PackInfo,
    ];
    // PackPos
    WriteU64(headerPayload, 0);
    // NumPackStreams
    WriteU64(headerPayload, 1);
    // Size
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedHeader.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);
    // Folder
    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0); // External = 0

    // Folder описывает цепочку кодеров.
    // NumCoders
    WriteU64(headerPayload, 1);

    // Coder info:
    //   MainByte:
    //     - IDSize = 1 (младшие 4 бита: 1 => 1 байт; 0 считается ошибкой)
    //     - IsSimple = 1 (один входной и один выходной поток)
    //     - HasProperties = 1 (есть свойства кодера)
    //     - HasAttributes = 0
    //     - MoreAlternativeMethods = 0
    //
    // Важно: в 7z флаг "simple" кодируется как (MainByte & 0x10) == 0.
    headerPayload.Add(0b0010_0001); // MainByte: idSize=1 (младшие 4 бита), hasProps=1
                                    // MethodID (LZMA2 = 0x21)
    headerPayload.Add(0x21);
    // Properties
    headerPayload.Add(1); // properties size
    headerPayload.Add(lzma2PropertiesByte);

    // BindPairs и PackedStreamIndices можно не писать:
    //  - при одном выходном потоке NumBindPairs == 0,
    //  - при NumPackedStreams == 1 индекс подразумевается равным 0.

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytes.Length);

    headerPayload.Add(SevenZipNid.End); // End of UnpackInfo

    // SubStreamsInfo
    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End of MainStreamsInfo

    // FilesInfo
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1); // NumFiles

    // Name property.
    headerPayload.Add(SevenZipNid.Name);

    // Данные свойства "Name" идут как:
    //   Size (u64)
    //   External (byte)
    //   UTF-16LE null-terminated strings
    // Здесь: один файл.
    var nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(headerPayload, (ulong)(1 + nameBytes.Length));
    headerPayload.Add(0); // External = 0
    headerPayload.AddRange(nameBytes);

    headerPayload.Add(SevenZipNid.End); // End of FilesInfo

    headerPayload.Add(SevenZipNid.End); // End of Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
    // next header сразу после signature header
    ulong nextHeaderOffset = (ulong)packedHeader.Length;
    ulong nextHeaderSize = (ulong)nextHeader.Length;

    SevenZipSignatureHeader sig = new(
        NextHeaderOffset: nextHeaderOffset,
        NextHeaderSize: nextHeaderSize,
        NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    // ----- Archive = [SignatureHeader][PackedStreams][NextHeader] -----
    var archive = new byte[sigBytes.Length + packedHeader.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedHeader.CopyTo(archive, sigBytes.Length);
    nextHeader.CopyTo(archive, sigBytes.Length + packedHeader.Length);

    return archive;
  }
}
