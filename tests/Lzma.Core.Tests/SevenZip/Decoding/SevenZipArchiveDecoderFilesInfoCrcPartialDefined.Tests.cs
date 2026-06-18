using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderFilesInfoCrcPartialDefinedTests
{
  [Fact]
  public void DecodeToArray_FilesInfoCrcPartialDefined_DataFileOnly_Ok()
  {
    byte[] dataBytes = MakePattern(128, mul: 31, add: 7);
    uint dataCrc = Crc32.Compute(dataBytes);

    byte[] archive = BuildArchive_EmptyFile_And_DataFile_Copy(
        emptyFileName: "empty.bin",
        dataFileName: "data.bin",
        dataBytes: dataBytes,
        crcDefinedMask: 0x40, // file[1] only
        crcValuesInDefinedOrder: [dataCrc]);

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

    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(dataBytes, byName["data.bin"].Bytes);
  }

  [Fact]
  public void DecodeToArray_FilesInfoCrcPartialDefined_DataFileOnly_Mismatch_InvalidData()
  {
    byte[] dataBytes = MakePattern(128, mul: 31, add: 7);
    uint wrongCrc = Crc32.Compute(dataBytes) ^ 0xFFFFFFFFu;

    byte[] archive = BuildArchive_EmptyFile_And_DataFile_Copy(
        emptyFileName: "empty.bin",
        dataFileName: "data.bin",
        dataBytes: dataBytes,
        crcDefinedMask: 0x40, // file[1] only
        crcValuesInDefinedOrder: [wrongCrc]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeToArray_FilesInfoCrcPartialDefined_EmptyFileOnly_Ok()
  {
    byte[] dataBytes = MakePattern(128, mul: 31, add: 7);
    uint emptyCrc = Crc32.Compute([]);

    byte[] archive = BuildArchive_EmptyFile_And_DataFile_Copy(
        emptyFileName: "empty.bin",
        dataFileName: "data.bin",
        dataBytes: dataBytes,
        crcDefinedMask: 0x80, // file[0] only
        crcValuesInDefinedOrder: [emptyCrc]);

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

    Assert.Empty(byName["empty.bin"].Bytes);
    Assert.Equal(dataBytes, byName["data.bin"].Bytes);
  }

  [Fact]
  public void DecodeToArray_FilesInfoCrcPartialDefined_EmptyFileOnly_Mismatch_InvalidData()
  {
    byte[] dataBytes = MakePattern(128, mul: 31, add: 7);
    const uint wrongEmptyCrc = 0x11223344u;

    byte[] archive = BuildArchive_EmptyFile_And_DataFile_Copy(
        emptyFileName: "empty.bin",
        dataFileName: "data.bin",
        dataBytes: dataBytes,
        crcDefinedMask: 0x80, // file[0] only
        crcValuesInDefinedOrder: [wrongEmptyCrc]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  private static byte[] BuildArchive_EmptyFile_And_DataFile_Copy(
      string emptyFileName,
      string dataFileName,
      byte[] dataBytes,
      byte crcDefinedMask,
      uint[] crcValuesInDefinedOrder)
  {
    byte[] nextHeader = BuildNextHeader_EmptyFile_And_DataFile_Copy(
        packSize: dataBytes.Length,
        unpackSize: dataBytes.Length,
        emptyFileName: emptyFileName,
        dataFileName: dataFileName,
        crcDefinedMask: crcDefinedMask,
        crcValuesInDefinedOrder: crcValuesInDefinedOrder);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)dataBytes.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + dataBytes.Length + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(dataBytes, 0, archive, SevenZipSignatureHeader.Size, dataBytes.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + dataBytes.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeader_EmptyFile_And_DataFile_Copy(
      int packSize,
      int unpackSize,
      string emptyFileName,
      string dataFileName,
      byte crcDefinedMask,
      uint[] crcValuesInDefinedOrder)
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
    WriteU64(h, 2); // NumFiles

    // file[0] = empty, file[1] = data
    WriteEmptyStreamPropertyForFirstFile(h);
    WriteEmptyFilePropertyForSingleEmpty(h);
    WriteNameProperty(h, emptyFileName, dataFileName);
    WriteFilesInfoCrcPropertyPartial(h, crcDefinedMask, crcValuesInDefinedOrder);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static void WriteEmptyStreamPropertyForFirstFile(List<byte> h)
  {
    h.Add(SevenZipNid.EmptyStream);

    // payload = 1 byte bitmask for 2 files: file[0]=true, file[1]=false => 1000_0000
    WriteU64(h, 1);
    h.Add(0x80);
  }

  private static void WriteEmptyFilePropertyForSingleEmpty(List<byte> h)
  {
    h.Add(SevenZipNid.EmptyFile);

    // Среди empty-stream элементов один файл, и он именно file, не directory.
    WriteU64(h, 1);
    h.Add(0x80);
  }

  private static void WriteNameProperty(List<byte> h, string emptyFileName, string dataFileName)
  {
    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(emptyFileName + "\0" + dataFileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);
  }

  private static void WriteFilesInfoCrcPropertyPartial(List<byte> h, byte definedMask, uint[] crcValuesInDefinedOrder)
  {
    h.Add(SevenZipNid.Crc);

    // payload:
    // [0] AllAreDefined = 0
    // [1] defined-bitset for 2 files
    // [2..] CRC32 only for defined files, в порядке файлов
    int payloadSize = 1 + 1 + crcValuesInDefinedOrder.Length * 4;
    WriteU64(h, (ulong)payloadSize);

    h.Add(0x00);          // AllAreDefined = 0
    h.Add(definedMask);   // bitset over 2 files

    foreach (uint crc in crcValuesInDefinedOrder)
      WriteU32(h, crc);
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
