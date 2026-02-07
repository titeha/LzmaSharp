using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipArchiveDecoderTests
{
  [Fact]
  public void DecodeSingleFile_ОдинФайл_ОдинFolder_Lzma2Copy_ВозвращаетИсходныеБайты()
  {
    byte[] fileBytes = [
        0, 1, 2, 3, 4, 5, 6, 7,
            8, 9, 255, 254, 253, 0, 0, 1,
            65, 66, 67, 68, 69, 70,
        ];

    const string fileName = "file.bin";

    byte[] archive = Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(fileBytes, fileName);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] decoded,
        out string? decodedName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(fileBytes, decoded);
    Assert.Equal(fileName, decodedName);
    Assert.True(bytesConsumed > 0);
    Assert.True(bytesConsumed <= archive.Length);
  }

  private static byte[] Build7zArchive_SingleFile_SingleFolder_Lzma2Copy(byte[] fileBytes, string fileName)
  {
    const int dictionarySize = 1 << 20;
    // Для COPY-чанков LZMA2 размер payload кодируется в 16 битах и должен быть в диапазоне [1..65536].
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
        fileName);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0, // заполним ниже
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    // CRC считается по байтам StartHeader (NextHeaderOffset/Size/Crc) и записывается в поле StartHeaderCrc.
    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    // SignatureHeader
    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);
    archive.AddRange(sigBuf.ToArray());

    // PackedStreams
    archive.AddRange(packedStreams);

    // NextHeader (Header)
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderSingleFolderSingleStream(ulong[] packSizes, ulong folderUnpackSize, SevenZipCoderInfo coder, string fileName)
  {
    // Содержимое Header:
    // Header
    //   MainStreamsInfo
    //     PackInfo
    //     UnpackInfo
    //     SubStreamsInfo (пусто)
    //   FilesInfo

    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(header, packSizes, folderUnpackSize, coder);
    WriteFilesInfo(header, fileName);

    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(List<byte> output, ulong[] packSizes, ulong folderUnpackSize, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packSizes);
    WriteUnpackInfo(output, folderUnpackSize, coder);

    // SubStreamsInfo: на этом шаге делаем пустую секцию.
    WriteSubStreamsInfoEmpty(output);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong[] packSizes)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    // PackPos
    WriteEncodedUInt64(output, 0);

    // NumPackStreams
    WriteEncodedUInt64(output, (ulong)packSizes.Length);

    // Size
    WriteNid(output, SevenZipNid.Size);
    foreach (ulong s in packSizes)
      WriteEncodedUInt64(output, s);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(List<byte> output, ulong folderUnpackSize, SevenZipCoderInfo coder)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    // Folder
    WriteNid(output, SevenZipNid.Folder);

    // NumFolders
    WriteEncodedUInt64(output, 1);

    // External
    WriteByte(output, 0);

    WriteFolder(output, coder);

    // CodersUnpackSize
    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, folderUnpackSize);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolder(List<byte> output, SevenZipCoderInfo coder)
  {
    // NumCoders
    WriteEncodedUInt64(output, 1);

    WriteCoderInfo(output, coder);
  }

  private static void WriteCoderInfo(List<byte> output, SevenZipCoderInfo coder)
  {
    int methodIdSize = coder.MethodId.Length;
    Assert.InRange(methodIdSize, 1, 15);

    bool isComplexCoder = coder.NumInStreams != 1 || coder.NumOutStreams != 1;
    bool hasProperties = coder.Properties.Length != 0;

    // mainByte:
    //  - младшие 4 бита: размер MethodID (1..15)
    //  - 0x10: complex coder (есть NumInStreams/NumOutStreams)
    //  - 0x20: есть Properties
    byte mainByte = (byte)(
        (methodIdSize & 0x0F) |
        (isComplexCoder ? 0x10 : 0) |
        (hasProperties ? 0x20 : 0));

    WriteByte(output, mainByte);

    // MethodID bytes
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

  private static void WriteSubStreamsInfoEmpty(List<byte> output)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(List<byte> output, string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    // NumFiles
    WriteEncodedUInt64(output, 1);

    // Name
    WriteFileInfoNames(output, [fileName]);

    // End
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFileInfoNames(List<byte> output, string[] names)
  {
    // Property ID
    WriteNid(output, SevenZipNid.Name);

    // Агрегируем имена в UTF-16LE строку с '\0' после каждого.
    var bytes = new List<byte>();
    foreach (string n in names)
    {
      byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(n);
      bytes.AddRange(nameBytes);
      bytes.Add(0);
      bytes.Add(0);
    }

    // Property size
    WriteEncodedUInt64(output, (ulong)(1 + bytes.Count));

    // External = 0
    WriteByte(output, 0);

    // Payload
    output.AddRange(bytes);

    // End of property
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buf = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, buf, out int bytesWritten);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    Assert.True(bytesWritten > 0);

    output.AddRange(buf[..bytesWritten].ToArray());
  }

  private static void WriteByte(List<byte> output, byte value) => output.Add(value);

  private static void WriteNid(List<byte> output, byte nid) => output.Add(nid);
}
