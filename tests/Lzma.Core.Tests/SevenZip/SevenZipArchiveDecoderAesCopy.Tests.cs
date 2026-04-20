using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderAesCopyTests
{
  [Fact]
  public void DecodeSingleFileToArray_AesCopyArchive_СПаролем_ВозвращаетИсходныеБайты()
  {
    byte[] plain = new byte[16];

    // AES-256-CBC:
    // key = 32 нулевых байта,
    // iv = 16 нулевых байт,
    // plaintext = 16 нулевых байт.
    byte[] encrypted = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    byte[] archive = Build7zArchive_SingleFile_AesThenCopy(
        packedStreams: encrypted,
        fileName: "aes.bin",
        aesUnpackSize: (ulong)plain.Length,
        finalUnpackSize: (ulong)plain.Length,
        folderCrc: Crc32.Compute(plain));

    using SevenZipPassword password = SevenZipPassword.FromString("");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(plain, decoded);
    Assert.Equal("aes.bin", decodedName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeSingleFileToArray_AesCopyArchive_БезПароля_ВозвращаетNotSupported()
  {
    byte[] encrypted = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    byte[] archive = Build7zArchive_SingleFile_AesThenCopy(
        packedStreams: encrypted,
        fileName: "aes.bin",
        aesUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        folderCrc: Crc32.Compute(new byte[16]));

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
    Assert.Equal(string.Empty, decodedName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void DecodeSingleFileToArray_AesCopyArchive_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] encrypted = Convert.FromHexString("DC95C078A2408989AD48A21492842087");

    byte[] archive = Build7zArchive_SingleFile_AesThenCopy(
        packedStreams: encrypted,
        fileName: "aes.bin",
        aesUnpackSize: 16UL,
        finalUnpackSize: 16UL,
        folderCrc: Crc32.Compute(new byte[16]));

    using SevenZipPassword password = SevenZipPassword.FromString("wrong");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] decoded,
        fileName: out string decodedName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
    Assert.Equal(string.Empty, decodedName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  private static byte[] Build7zArchive_SingleFile_AesThenCopy(
      byte[] packedStreams,
      string fileName,
      ulong aesUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    byte[] nextHeader = BuildHeaderSingleFolderAesThenCopy(
        packSize: (ulong)packedStreams.Length,
        aesUnpackSize: aesUnpackSize,
        finalUnpackSize: finalUnpackSize,
        fileName: fileName,
        folderCrc: folderCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sigHeader = new SevenZipSignatureHeader(
        VersionMajor: 0,
        VersionMinor: 4,
        StartHeaderCrc: 0,
        NextHeaderOffset: (ulong)packedStreams.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(sigHeader.GetStartHeaderBytes());
    sigHeader = sigHeader with { StartHeaderCrc = startHeaderCrc };

    var archive = new List<byte>(
        SevenZipSignatureHeader.TotalSize + packedStreams.Length + nextHeader.Length);

    Span<byte> sigBuf = stackalloc byte[SevenZipSignatureHeader.TotalSize];
    sigHeader.Write(sigBuf);

    archive.AddRange(sigBuf.ToArray());
    archive.AddRange(packedStreams);
    archive.AddRange(nextHeader);

    return [.. archive];
  }

  private static byte[] BuildHeaderSingleFolderAesThenCopy(
      ulong packSize,
      ulong aesUnpackSize,
      ulong finalUnpackSize,
      string fileName,
      uint? folderCrc)
  {
    var header = new List<byte>(256);

    WriteNid(header, SevenZipNid.Header);

    WriteStreamsInfo(
        header,
        packSize: packSize,
        aesUnpackSize: aesUnpackSize,
        finalUnpackSize: finalUnpackSize,
        folderCrc: folderCrc);

    WriteFilesInfo(header, fileName);

    WriteNid(header, SevenZipNid.End);

    return [.. header];
  }

  private static void WriteStreamsInfo(
      List<byte> output,
      ulong packSize,
      ulong aesUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.MainStreamsInfo);

    WritePackInfo(output, packSize);
    WriteUnpackInfo(output, aesUnpackSize, finalUnpackSize, folderCrc);
    WriteSubStreamsInfoEmpty(output);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WritePackInfo(List<byte> output, ulong packSize)
  {
    WriteNid(output, SevenZipNid.PackInfo);

    WriteEncodedUInt64(output, 0);
    WriteEncodedUInt64(output, 1);

    WriteNid(output, SevenZipNid.Size);
    WriteEncodedUInt64(output, packSize);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteUnpackInfo(
      List<byte> output,
      ulong aesUnpackSize,
      ulong finalUnpackSize,
      uint? folderCrc)
  {
    WriteNid(output, SevenZipNid.UnpackInfo);

    WriteNid(output, SevenZipNid.Folder);
    WriteEncodedUInt64(output, 1);
    WriteByte(output, 0);

    WriteFolderAesThenCopy(output);

    WriteNid(output, SevenZipNid.CodersUnpackSize);
    WriteEncodedUInt64(output, aesUnpackSize);
    WriteEncodedUInt64(output, finalUnpackSize);

    if (folderCrc.HasValue)
    {
      WriteNid(output, SevenZipNid.Crc);
      WriteByte(output, 1);
      WriteUInt32LE(output, folderCrc.Value);
    }

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFolderAesThenCopy(List<byte> output)
  {
    var aesCoder = new SevenZipCoderInfo(
        methodId: [0x06, 0xF1, 0x07, 0x01],
        properties: [SevenZipAesCoder.DirectKeyNumCyclesPower],
        numInStreams: 1,
        numOutStreams: 1);

    var copyCoder = new SevenZipCoderInfo(
        methodId: [0x00],
        properties: [],
        numInStreams: 1,
        numOutStreams: 1);

    WriteEncodedUInt64(output, 2);

    WriteCoderInfo(output, aesCoder);
    WriteCoderInfo(output, copyCoder);

    // AES.out0 -> Copy.in1
    WriteEncodedUInt64(output, 1);
    WriteEncodedUInt64(output, 0);

    // PackedStreamIndices не пишем:
    // при NumPackedStreams == 1 parser вычисляет единственный unbound InIndex сам.
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

  private static void WriteSubStreamsInfoEmpty(List<byte> output)
  {
    WriteNid(output, SevenZipNid.SubStreamsInfo);
    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFilesInfo(List<byte> output, string fileName)
  {
    WriteNid(output, SevenZipNid.FilesInfo);

    WriteEncodedUInt64(output, 1);
    WriteFileInfoNames(output, [fileName]);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteFileInfoNames(List<byte> output, string[] names)
  {
    WriteNid(output, SevenZipNid.Name);

    var bytes = new List<byte>();
    foreach (string name in names)
    {
      byte[] nameBytes = System.Text.Encoding.Unicode.GetBytes(name);
      bytes.AddRange(nameBytes);
      bytes.Add(0);
      bytes.Add(0);
    }

    WriteEncodedUInt64(output, (ulong)(1 + bytes.Count));
    WriteByte(output, 0);
    output.AddRange(bytes);

    WriteNid(output, SevenZipNid.End);
  }

  private static void WriteEncodedUInt64(List<byte> output, ulong value)
  {
    Span<byte> buf = stackalloc byte[9];

    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(
        value,
        buf,
        out int bytesWritten);

    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    Assert.True(bytesWritten > 0);

    output.AddRange(buf[..bytesWritten].ToArray());
  }

  private static void WriteByte(List<byte> output, byte value)
  {
    output.Add(value);
  }

  private static void WriteNid(List<byte> output, byte nid)
  {
    output.Add(nid);
  }

  private static void WriteUInt32LE(List<byte> output, uint value)
  {
    output.Add((byte)value);
    output.Add((byte)(value >> 8));
    output.Add((byte)(value >> 16));
    output.Add((byte)(value >> 24));
  }
}
