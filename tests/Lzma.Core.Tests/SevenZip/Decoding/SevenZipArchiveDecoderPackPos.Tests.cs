using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderPackPosTests
{
  [Fact]
  public void DecodeSingleFileToArray_PackInfoPackPosNonZero_Lzma2Copy_Ok()
  {
    byte[] plain = new byte[128];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    const string fileName = "file.bin";
    const int dictionarySize = 1 << 20;

    // LZMA2 COPY поток (LZMA2 stream).
    byte[] packed = Lzma2CopyEncoder.Encode(plain, dictionarySize, out byte lzma2PropertiesByte);

    // Делаем “мусорный” префикс в packed area, чтобы PackPos был ненулевым.
    byte[] prefix = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

    byte[] nextHeader = BuildNextHeader_SingleFile_Lzma2(
      packPos: prefix.Length,
      packSize: packed.Length,
      unpackSize: plain.Length,
      fileName: fileName,
      lzma2PropertiesByte: lzma2PropertiesByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)(prefix.Length + packed.Length),
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + prefix.Length + packed.Length + nextHeader.Length];
    sig.Write(archive);

    Buffer.BlockCopy(prefix, 0, archive, SevenZipSignatureHeader.Size, prefix.Length);
    Buffer.BlockCopy(packed, 0, archive, SevenZipSignatureHeader.Size + prefix.Length, packed.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + prefix.Length + packed.Length, nextHeader.Length);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(fileName, decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] BuildNextHeader_SingleFile_Lzma2(
    int packPos,
    int packSize,
    int unpackSize,
    string fileName,
    byte lzma2PropertiesByte)
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
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External = 0

    // Folder: NumCoders = 1
    WriteU64(h, 1);

    // Coder: LZMA2 (0x21) + props (1 byte)
    h.Add(0x21); // mainByte: idSize=1, hasProps=1, isComplexCoder=0
    h.Add(0x21); // methodId
    WriteU64(h, 1); // props size
    h.Add(lzma2PropertiesByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo (MainStreamsInfo)

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
}
