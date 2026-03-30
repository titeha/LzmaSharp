using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderPackInfoCrcPartialDefinedTests
{
  [Fact]
  public void DecodeToArray_TwoCopyFolders_PackInfoCrcPartialDefined_SecondStreamOnly_Ok()
  {
    byte[] bytes1 = MakePattern(64, mul: 17, add: 3);
    byte[] bytes2 = MakePattern(96, mul: 29, add: 5);

    uint crc2 = Crc32.Compute(bytes2);

    byte[] archive = BuildArchiveTwoFilesTwoCopyFoldersWithPartialPackCrc(
        fileName1: "a.bin",
        fileBytes1: bytes1,
        fileName2: "b.bin",
        fileBytes2: bytes2,
        definedMask: 0x40, // pack stream #1 only
        crcValuesInDefinedOrder: [crc2]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (SevenZipDecodedFile f in files)
      byName.Add(f.Name.Replace('\\', '/'), f);

    Assert.Equal(bytes1, byName["a.bin"].Bytes);
    Assert.Equal(bytes2, byName["b.bin"].Bytes);
  }

  [Fact]
  public void DecodeToArray_TwoCopyFolders_PackInfoCrcPartialDefined_SecondStreamOnly_Mismatch_InvalidData()
  {
    byte[] bytes1 = MakePattern(64, mul: 17, add: 3);
    byte[] bytes2 = MakePattern(96, mul: 29, add: 5);

    uint wrongCrc2 = Crc32.Compute(bytes2) ^ 0xFFFFFFFFu;

    byte[] archive = BuildArchiveTwoFilesTwoCopyFoldersWithPartialPackCrc(
        fileName1: "a.bin",
        fileBytes1: bytes1,
        fileName2: "b.bin",
        fileBytes2: bytes2,
        definedMask: 0x40, // pack stream #1 only
        crcValuesInDefinedOrder: [wrongCrc2]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  private static byte[] BuildArchiveTwoFilesTwoCopyFoldersWithPartialPackCrc(
      string fileName1,
      byte[] fileBytes1,
      string fileName2,
      byte[] fileBytes2,
      byte definedMask,
      uint[] crcValuesInDefinedOrder)
  {
    byte[] nextHeader = BuildNextHeaderTwoFilesTwoCopyFoldersWithPartialPackCrc(
        packSize1: fileBytes1.Length,
        packSize2: fileBytes2.Length,
        unpackSize1: fileBytes1.Length,
        unpackSize2: fileBytes2.Length,
        fileName1: fileName1,
        fileName2: fileName2,
        definedMask: definedMask,
        crcValuesInDefinedOrder: crcValuesInDefinedOrder);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)(fileBytes1.Length + fileBytes2.Length),
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + fileBytes1.Length + fileBytes2.Length + nextHeader.Length];

    sig.Write(archive);

    int pos = SevenZipSignatureHeader.Size;
    Buffer.BlockCopy(fileBytes1, 0, archive, pos, fileBytes1.Length);
    pos += fileBytes1.Length;

    Buffer.BlockCopy(fileBytes2, 0, archive, pos, fileBytes2.Length);
    pos += fileBytes2.Length;

    Buffer.BlockCopy(nextHeader, 0, archive, pos, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderTwoFilesTwoCopyFoldersWithPartialPackCrc(
      int packSize1,
      int packSize2,
      int unpackSize1,
      int unpackSize2,
      string fileName1,
      string fileName2,
      byte definedMask,
      uint[] crcValuesInDefinedOrder)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 2); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize1);
    WriteU64(h, (ulong)packSize2);

    // PackInfo.kCRC с AllAreDefined = 0.
    h.Add(SevenZipNid.Crc);
    h.Add(0x00);        // AllAreDefined = 0
    h.Add(definedMask); // bitset для 2 потоков

    foreach (uint crc in crcValuesInDefinedOrder)
      WriteU32(h, crc);

    h.Add(SevenZipNid.End);

    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 2);     // NumFolders
    h.Add(0x00);        // External = 0

    WriteCopyFolder(h);
    WriteCopyFolder(h);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize1);
    WriteU64(h, (ulong)unpackSize2);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2); // NumFiles

    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName1 + "\0" + fileName2 + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static void WriteCopyFolder(List<byte> h)
  {
    WriteU64(h, 1); // NumCoders
    h.Add(0x01);    // mainByte: idSize=1, простой coder
    h.Add(0x00);    // MethodId = Copy
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
