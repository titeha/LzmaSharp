using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderFilesInfoCrcTests
{
  [Fact]
  public void DecodeSingleFileToArray_Copy_WithFilesInfoCrc_Ok()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    uint fileCrc = Crc32.Compute(plain);

    byte[] archive = BuildArchiveSingleFileCopyWithFilesInfoCrc(
        plain,
        fileName: "file.bin",
        fileCrc: fileCrc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("file.bin", fileName);
    Assert.Equal(plain, fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_Copy_WithFilesInfoCrcMismatch_InvalidData()
  {
    byte[] plain = MakePattern(128, mul: 31, add: 7);
    uint fileCrc = Crc32.Compute(plain) ^ 0xFFFFFFFFu;

    byte[] archive = BuildArchiveSingleFileCopyWithFilesInfoCrc(
        plain,
        fileName: "file.bin",
        fileCrc: fileCrc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out _,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeSingleFileToArray_EmptyFile_WithFilesInfoCrc_Ok()
  {
    uint emptyCrc = Crc32.Compute([]);

    byte[] archive = BuildArchiveSingleEmptyFileWithFilesInfoCrc(
        fileName: "empty.bin",
        fileCrc: emptyCrc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("empty.bin", fileName);
    Assert.Empty(fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_EmptyFile_WithFilesInfoCrcMismatch_InvalidData()
  {
    const uint wrongCrc = 0x11223344u;

    byte[] archive = BuildArchiveSingleEmptyFileWithFilesInfoCrc(
        fileName: "empty.bin",
        fileCrc: wrongCrc);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out _,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  private static byte[] BuildArchiveSingleFileCopyWithFilesInfoCrc(
      byte[] plain,
      string fileName,
      uint fileCrc)
  {
    byte[] nextHeader = BuildNextHeaderSingleFileCopyWithFilesInfoCrc(
        packSize: plain.Length,
        unpackSize: plain.Length,
        fileName: fileName,
        fileCrc: fileCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)plain.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + plain.Length + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(plain, 0, archive, SevenZipSignatureHeader.Size, plain.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + plain.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildArchiveSingleEmptyFileWithFilesInfoCrc(string fileName, uint fileCrc)
  {
    byte[] nextHeader = BuildNextHeaderSingleEmptyFileWithFilesInfoCrc(fileName, fileCrc);
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: 0,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderSingleFileCopyWithFilesInfoCrc(
      int packSize,
      int unpackSize,
      string fileName,
      uint fileCrc)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            // PackInfo
            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);

    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);   // NumFolders
    h.Add(0x00);      // External = 0
    WriteU64(h, 1);   // NumCoders

    // Copy coder: MethodId = { 00 }, без props.
    h.Add(0x01);      // mainByte: idSize=1, простой coder
    h.Add(0x00);      // methodId = Copy

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1); // NumFiles

    WriteNameProperty(h, fileName);
    WriteFilesInfoCrcProperty(h, fileCrc);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static byte[] BuildNextHeaderSingleEmptyFileWithFilesInfoCrc(string fileName, uint fileCrc)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.FilesInfo,
        ];

    WriteU64(h, 1); // NumFiles

    // EmptyStream: один файл без потока.
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x80);

    // EmptyFile: этот EmptyStream является именно файлом, а не директорией.
    h.Add(SevenZipNid.EmptyFile);
    WriteU64(h, 1);
    h.Add(0x80);

    WriteNameProperty(h, fileName);
    WriteFilesInfoCrcProperty(h, fileCrc);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static void WriteNameProperty(List<byte> h, string fileName)
  {
    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);
  }

  private static void WriteFilesInfoCrcProperty(List<byte> h, uint fileCrc)
  {
    h.Add(SevenZipNid.Crc);

    // Property payload:
    // [0] AllAreDefined = 1
    // [1..4] CRC32 LE
    WriteU64(h, 5);
    h.Add(0x01);
    WriteU32(h, fileCrc);
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    byte[] bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static void WriteU32(List<byte> dst, uint value)
  {
    Span<byte> tmp = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(tmp, value);

    dst.Add(tmp[0]);
    dst.Add(tmp[1]);
    dst.Add(tmp[2]);
    dst.Add(tmp[3]);
  }
}
