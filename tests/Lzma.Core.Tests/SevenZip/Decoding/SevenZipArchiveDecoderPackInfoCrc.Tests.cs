using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderPackInfoCrcTests
{
  [Fact]
  public void DecodeSingleFileToArray_PackInfoCrc_Ok()
  {
    byte[] plain = new byte[128];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;

    byte[] packed = Lzma2CopyEncoder.Encode(plain, dictionarySize, out byte lzma2PropertiesByte);
    uint packCrc = Crc32.Compute(packed);

    byte[] nextHeader = BuildNextHeader_SingleFile_Lzma2_WithPackCrc(
        packPos: 0,
        packSize: packed.Length,
        unpackSize: plain.Length,
        fileName: fileName,
        lzma2PropertiesByte: lzma2PropertiesByte,
        packCrc: packCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packed.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packed.Length + nextHeader.Length];
    sig.Write(archive);
    Buffer.BlockCopy(packed, 0, archive, SevenZipSignatureHeader.Size, packed.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + packed.Length, nextHeader.Length);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive, out byte[] decodedBytes, out string decodedName, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_PackInfoCrcMismatch_InvalidData()
  {
    byte[] plain = new byte[128];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;

    byte[] packed = Lzma2CopyEncoder.Encode(plain, dictionarySize, out byte lzma2PropertiesByte);
    uint packCrc = Crc32.Compute(packed);

    byte[] nextHeader = BuildNextHeader_SingleFile_Lzma2_WithPackCrc(
        packPos: 0,
        packSize: packed.Length,
        unpackSize: plain.Length,
        fileName: fileName,
        lzma2PropertiesByte: lzma2PropertiesByte,
        packCrc: packCrc);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packed.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packed.Length + nextHeader.Length];
    sig.Write(archive);
    Buffer.BlockCopy(packed, 0, archive, SevenZipSignatureHeader.Size, packed.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + packed.Length, nextHeader.Length);

    // Портим байт внутри payload LZMA2-copy (не трогаем служебные первые 3 байта чанка).
    int corruptPos = SevenZipSignatureHeader.Size + 3 + 10;
    archive[corruptPos] ^= 0xFF;

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive, out _, out _, out _);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
  }

  private static byte[] BuildNextHeader_SingleFile_Lzma2_WithPackCrc(
      int packPos,
      int packSize,
      int unpackSize,
      string fileName,
      byte lzma2PropertiesByte,
      uint packCrc)
  {
    List<byte> h = new(256)
        {
            SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            // PackInfo
            SevenZipNid.PackInfo
        };

    WriteU64(h, (ulong)packPos);
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);

    // PackInfo.kCRC (Digests)
    h.Add(SevenZipNid.Crc);
    h.Add(0x01); // AllAreDefined = 1
    WriteU32(h, packCrc);

    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);    // NumFolders
    h.Add(0x00);       // External = 0
    WriteU64(h, 1);    // NumCoders

    // Coder: LZMA2 (0x21) + props (1 byte)
    h.Add(0x21);       // mainByte: idSize=1, hasProps=1, isComplexCoder=0
    h.Add(0x21);       // methodId
    WriteU64(h, 1);    // props size
    h.Add(lzma2PropertiesByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1); // NumFiles
    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

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
