using System;
using System.Collections.Generic;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSubStreamsCrcTests
{
  [Fact]
  public void DecodeSingleFile_Lzma2Copy_WithSubStreamsCrc_Ok()
  {
    byte[] fileBytes = new byte[4096];
    for (int i = 0; i < fileBytes.Length; i++)
      fileBytes[i] = (byte)i;

    const string fileName = "file.bin";
    uint crc = Crc32.Compute(fileBytes);

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy_WithSubStreamCrc(fileBytes, fileName, subStreamCrc: crc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] decoded,
        out string decodedName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(fileBytes, decoded);
    Assert.Equal(fileName, decodedName);
    Assert.True(bytesConsumed > 0);
    Assert.True(bytesConsumed <= archive.Length);
  }

  [Fact]
  public void DecodeSingleFile_Lzma2Copy_WithSubStreamsCrcMismatch_InvalidData()
  {
    byte[] fileBytes = new byte[4096];
    for (int i = 0; i < fileBytes.Length; i++)
      fileBytes[i] = (byte)(i * 31);

    const string fileName = "file.bin";
    uint crc = Crc32.Compute(fileBytes);

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy_WithSubStreamCrc(fileBytes, fileName, subStreamCrc: crc);

    // Портим 1 байт внутри payload первого COPY-чанка LZMA2:
    // - не трогаем заголовок чанка (3 байта)
    // - не трогаем EndMarker (0x00 в конце),
    // чтобы распаковка прошла, но данные стали другими => ловим CRC.
    byte[] corrupted = (byte[])archive.Clone();
    int packedStart = SevenZipSignatureHeader.TotalSize;

    // packedStreams layout для LZMA2 COPY: [control][sizeHi][sizeLo][payload...][0x00]
    corrupted[packedStart + 3 + 10] ^= 0xFF;

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        corrupted,
        out _,
        out _,
        out _);

    // ДО фикса сейчас обычно будет Ok (потому что expectedCrc не сравнивается).
    // После фикса должно стать InvalidData.
    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2Copy_WithSubStreamCrc(
      byte[] fileBytes,
      string fileName,
      uint subStreamCrc)
  {
    const int dictionarySize = 1 << 20;

    // Для COPY-чанков LZMA2 payload в диапазоне [1..65536]
    const int maxChunkPayloadSize = 64 * 1024;

    byte[] packedStreams = Lzma2CopyEncoder.EncodeChunkedAuto(
        fileBytes,
        dictionarySize,
        maxChunkPayloadSize: maxChunkPayloadSize,
        out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeaderSingleFolderSingleStream(
        packSizes: [(ulong)packedStreams.Length],
        folderUnpackSize: (ulong)fileBytes.Length,
        coder: new SevenZipCoderInfo([0x21], [lzma2PropsByte], numInStreams: 1, numOutStreams: 1),
        fileName: fileName,
        subStreamCrc: subStreamCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0, // заполним ниже
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);
    archive.AddRange(sigBuf.ToArray());

    archive.AddRange(packedStreams);
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderSingleFolderSingleStream(
      ulong[] packSizes,
      ulong folderUnpackSize,
      SevenZipCoderInfo coder,
      string fileName,
      uint subStreamCrc)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(header, packSizes, folderUnpackSize, coder, subStreamCrc);
    WriteFilesInfo(header, fileName);

    WriteNid(header, SevenZipNid.End);
    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong[] packSizes,
      ulong folderUnpackSize,
      SevenZipCoderInfo coder,
      uint subStreamCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packSizes);
    WriteUnpackInfo(output, folderUnpackSize, coder);

    // ВАЖНО: именно SubStreamsInfo.kCRC (CRC по unpack-stream).
    WriteSubStreamsInfoCrc(output, subStreamCrc);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong[] packSizes)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    WriteEncodedUInt64(output, 0); // PackPos
    WriteEncodedUInt64(output, (ulong)packSizes.Length); // NumPackStreams

    WriteNid(output, SevenZipNid.Size);
    foreach (ulong s in packSizes)
      WriteEncodedUInt64(output, s);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong folderUnpackSize, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1); // NumFolders
    WriteByte(output, 0); // External

    WriteFolder(output, coder);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, folderUnpackSize);

    // Folder CRC (UnpackInfo.kCRC) специально НЕ пишем — проверяем именно stream CRC.
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteSubStreamsInfoCrc(List<byte> output, uint crc)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);

    WriteNid(output, SevenZipNid.Crc);
    WriteByte(output, 1); // AllAreDefined = 1 (у нас один unpack-stream)
    WriteUInt32LE(output, crc);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    WriteEncodedUInt64(output, 1); // NumCoders
    WriteCoderInfo(output, coder);
  }

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    Assert.InRange(methodIdSize, 1, 15);

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProperties = coder.Properties.Length != 0;

    byte mainByte = (byte)(
        (methodIdSize & 0x0F) |
        (isComplexCoder ? 0x10 : 0) |
        (hasProperties ? 0x20 : 0));

    WriteByte(output, mainByte);
    output.AddRange(coder.MethodId);

    if (isComplexCoder)
    {
      WriteEncodedUInt64(output, coder.NumInStreams);
      WriteEncodedUInt64(output, coder.NumOutStreams);
    }

    if (hasProperties)
    {
      WriteEncodedUInt64(output, (ulong)coder.Properties.Length);
      output.AddRange(coder.Properties);
    }
  }

  private static void WriteFilesInfo(List<byte> output, string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);
    WriteEncodedUInt64(output, 1); // NumFiles

    WriteFileInfoNames(output, [fileName]);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFileInfoNames(List<byte> output, string[] names)
  {
    WriteNid(output, SevenZipNid.Name);

    var bytes = new List<byte>();
    foreach (string n in names)
    {
      byte[] nameBytes = Encoding.Unicode.GetBytes(n);
      bytes.AddRange(nameBytes);
      bytes.Add(0);
      bytes.Add(0);
    }

    // Property size = 1 (External) + payload
    WriteEncodedUInt64(output, (ulong)(1 + bytes.Count));
    WriteByte(output, 0); // External = 0
    output.AddRange(bytes);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buf = stackalloc byte[9];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, buf, out int bytesWritten);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    output.AddRange(buf[..bytesWritten].ToArray());
  }

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);
  private static void WriteNid(List<byte> output, byte nid) => output.Add(nid);

  private static void WriteUInt32LE(List<byte> output, uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }
}
