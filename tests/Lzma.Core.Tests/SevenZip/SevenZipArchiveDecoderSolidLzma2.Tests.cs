using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSolidLzma2Tests
{
  [Fact]
  public void DecodeAllFilesToArray_Solid_ДваФайла_ОдинFolder_Lzma2LzmaChunked_Ok()
  {
    byte[] file1 = new byte[120];
    byte[] file2 = new byte[200];

    for (int i = 0; i < file1.Length; i++)
      file1[i] = (byte)(i * 13 + 1);

    for (int i = 0; i < file2.Length; i++)
      file2[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_SolidTwoFiles_Lzma2LzmaChunked(
      file1Name: "a.bin",
      file1Bytes: file1,
      file2Name: "b.bin",
      file2Bytes: file2,
      dictionarySize: 1 << 20,
      maxUnpackChunkSize: 64);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);

    Assert.Equal("a.bin", files[0].Name);
    Assert.Equal(file1, files[0].Bytes);

    Assert.Equal("b.bin", files[1].Name);
    Assert.Equal(file2, files[1].Bytes);
  }

  private static byte[] Build7z_SolidTwoFiles_Lzma2LzmaChunked(
    string file1Name,
    byte[] file1Bytes,
    string file2Name,
    byte[] file2Bytes,
    int dictionarySize,
    int maxUnpackChunkSize)
  {
    // 1) Solid payload = file1 || file2
    byte[] plain = new byte[file1Bytes.Length + file2Bytes.Length];
    Buffer.BlockCopy(file1Bytes, 0, plain, 0, file1Bytes.Length);
    Buffer.BlockCopy(file2Bytes, 0, plain, file1Bytes.Length, file2Bytes.Length);

    // 2) LZMA2 properties (dict)
    Assert.True(Lzma2Properties.TryEncode(dictionarySize, out byte lzma2PropsByte));

    // 3) Packed stream: LZMA2(LZMA literal-only), chunked
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);
    byte[] packedStream = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
      data: plain,
      lzmaProperties: lzmaProps,
      dictionarySize: dictionarySize,
      maxUnpackChunkSize: maxUnpackChunkSize,
      out _);

    // 4) NextHeader (plain Header)
    byte[] nextHeader = BuildHeader_TwoFiles_Solid(
      file1Name: file1Name,
      file1Size: (ulong)file1Bytes.Length,
      file2Name: file2Name,
      folderTotalUnpackSize: (ulong)plain.Length,
      packSize: (ulong)packedStream.Length,
      lzma2PropsByte: lzma2PropsByte);

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

  private static byte[] BuildHeader_TwoFiles_Solid(
    string file1Name,
    ulong file1Size,
    string file2Name,
    ulong folderTotalUnpackSize,
    ulong packSize,
    byte lzma2PropsByte)
  {
    var h = new List<byte>(512);

    h.Add(SevenZipNid.Header);
    h.Add(SevenZipNid.MainStreamsInfo);

    // PackInfo
    h.Add(SevenZipNid.PackInfo);
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
    h.Add(0x21);      // main byte: idSize=1 + hasProps
    h.Add(0x21);      // method id
    WriteU64(h, 1);   // props size
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

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);
    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
