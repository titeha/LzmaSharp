using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSubStreamsCrcPartialDefinedTests
{
  [Fact]
  public void DecodeToArray_SolidTwoFiles_Lzma2Copy_SubStreamsCrcPartialDefined_FirstOnly_Ok()
  {
    byte[] file1 = MakePattern(120, mul: 13, add: 1);
    byte[] file2 = MakePattern(200, mul: 17, add: 3);

    uint crc1 = Crc32.Compute(file1);

    byte[] archive = BuildArchive_SolidTwoFiles_Lzma2Copy_WithPartialSubStreamsCrc(
        file1Name: "a.bin",
        file1Bytes: file1,
        file2Name: "b.bin",
        file2Bytes: file2,
        definedMask: 0x80, // stream[0] only
        crcValuesInDefinedOrder: [crc1]);

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

    Assert.Equal(file1, byName["a.bin"].Bytes);
    Assert.Equal(file2, byName["b.bin"].Bytes);
  }

  [Fact]
  public void DecodeToArray_SolidTwoFiles_Lzma2Copy_SubStreamsCrcPartialDefined_FirstOnly_Mismatch_InvalidData()
  {
    byte[] file1 = MakePattern(120, mul: 13, add: 1);
    byte[] file2 = MakePattern(200, mul: 17, add: 3);

    uint wrongCrc1 = Crc32.Compute(file1) ^ 0xFFFFFFFFu;

    byte[] archive = BuildArchive_SolidTwoFiles_Lzma2Copy_WithPartialSubStreamsCrc(
        file1Name: "a.bin",
        file1Bytes: file1,
        file2Name: "b.bin",
        file2Bytes: file2,
        definedMask: 0x80, // stream[0] only
        crcValuesInDefinedOrder: [wrongCrc1]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeToArray_SolidTwoFiles_Lzma2Copy_SubStreamsCrcPartialDefined_SecondOnly_Ok()
  {
    byte[] file1 = MakePattern(120, mul: 13, add: 1);
    byte[] file2 = MakePattern(200, mul: 17, add: 3);

    uint crc2 = Crc32.Compute(file2);

    byte[] archive = BuildArchive_SolidTwoFiles_Lzma2Copy_WithPartialSubStreamsCrc(
        file1Name: "a.bin",
        file1Bytes: file1,
        file2Name: "b.bin",
        file2Bytes: file2,
        definedMask: 0x40, // stream[1] only
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

    Assert.Equal(file1, byName["a.bin"].Bytes);
    Assert.Equal(file2, byName["b.bin"].Bytes);
  }

  [Fact]
  public void DecodeToArray_SolidTwoFiles_Lzma2Copy_SubStreamsCrcPartialDefined_SecondOnly_Mismatch_InvalidData()
  {
    byte[] file1 = MakePattern(120, mul: 13, add: 1);
    byte[] file2 = MakePattern(200, mul: 17, add: 3);

    uint wrongCrc2 = Crc32.Compute(file2) ^ 0xFFFFFFFFu;

    byte[] archive = BuildArchive_SolidTwoFiles_Lzma2Copy_WithPartialSubStreamsCrc(
        file1Name: "a.bin",
        file1Bytes: file1,
        file2Name: "b.bin",
        file2Bytes: file2,
        definedMask: 0x40, // stream[1] only
        crcValuesInDefinedOrder: [wrongCrc2]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  private static byte[] BuildArchive_SolidTwoFiles_Lzma2Copy_WithPartialSubStreamsCrc(
      string file1Name,
      byte[] file1Bytes,
      string file2Name,
      byte[] file2Bytes,
      byte definedMask,
      uint[] crcValuesInDefinedOrder)
  {
    byte[] plain = new byte[file1Bytes.Length + file2Bytes.Length];
    Buffer.BlockCopy(file1Bytes, 0, plain, 0, file1Bytes.Length);
    Buffer.BlockCopy(file2Bytes, 0, plain, file1Bytes.Length, file2Bytes.Length);

    const int dictionarySize = 1 << 20;
    byte[] packedStream = Lzma2CopyEncoder.EncodeChunkedAuto(
        plain,
        dictionarySize,
        maxChunkPayloadSize: 64 * 1024,
        out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeader_TwoFiles_Solid_WithPartialSubStreamsCrc(
        file1Name: file1Name,
        file1Size: (ulong)file1Bytes.Length,
        file2Name: file2Name,
        folderTotalUnpackSize: (ulong)plain.Length,
        packSize: (ulong)packedStream.Length,
        lzma2PropsByte: lzma2PropsByte,
        definedMask: definedMask,
        crcValuesInDefinedOrder: crcValuesInDefinedOrder);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedStream.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStream.Length + nextHeader.Length];
    sig.Write(archive);
    Buffer.BlockCopy(packedStream, 0, archive, SevenZipSignatureHeader.Size, packedStream.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + packedStream.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildHeader_TwoFiles_Solid_WithPartialSubStreamsCrc(
      string file1Name,
      ulong file1Size,
      string file2Name,
      ulong folderTotalUnpackSize,
      ulong packSize,
      byte lzma2PropsByte,
      byte definedMask,
      uint[] crcValuesInDefinedOrder)
  {
    List<byte> h = new(512)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External=0

    // Folder: NumCoders=1
    WriteU64(h, 1);

    // Coder: LZMA2 (methodId=0x21), properties size=1
    h.Add(0x21); // main byte: idSize=1 + hasProps
    h.Add(0x21); // method id
    WriteU64(h, 1); // props size
    h.Add(lzma2PropsByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, folderTotalUnpackSize);
    h.Add(SevenZipNid.End); // End UnpackInfo

    // SubStreamsInfo: 2 unpack streams (2 файла)
    h.Add(SevenZipNid.SubStreamsInfo);

    h.Add(SevenZipNid.NumUnpackStream);
    WriteU64(h, 2);

    h.Add(SevenZipNid.Size);
    // Для 2 потоков пишется только размер первого, второй вычисляется как остаток.
    WriteU64(h, file1Size);

    h.Add(SevenZipNid.Crc);
    h.Add(0x00);       // AllAreDefined = 0
    h.Add(definedMask); // bitset over 2 streams

    foreach (uint crc in crcValuesInDefinedOrder)
      WriteU32LE(h, crc);

    h.Add(SevenZipNid.End); // End SubStreamsInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 2 files
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2);

    h.Add(SevenZipNid.Name);
    byte[] namesBytes = Encoding.Unicode.GetBytes(file1Name + "\0" + file2Name + "\0");
    WriteU64(h, (ulong)(1 + namesBytes.Length));
    h.Add(0); // External=0
    h.AddRange(namesBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
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
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }

  private static void WriteU32LE(List<byte> dst, uint value)
  {
    dst.Add((byte)value);
    dst.Add((byte)(value >> 8));
    dst.Add((byte)(value >> 16));
    dst.Add((byte)(value >> 24));
  }
}
