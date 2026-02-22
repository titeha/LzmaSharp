using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderTwoFoldersTwoPackStreamsTests
{
  [Fact]
  public void DecodeAllFilesToArray_TwoFolders_TwoPackStreams_PackPosNonZero_Ok()
  {
    byte[] file0 = new byte[80];
    byte[] file1 = new byte[250];

    for (int i = 0; i < file0.Length; i++)
      file0[i] = (byte)(i * 13 + 1);

    for (int i = 0; i < file1.Length; i++)
      file1[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_TwoFolders_TwoPackStreams(
      file0Name: "a.bin",
      file0Bytes: file0,
      file1Name: "b.bin",
      file1Bytes: file1,
      packPosPrefixLength: 13,
      dictionarySize: 1 << 20,
      maxUnpackChunkSize: 64);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);

    Assert.Equal("a.bin", files[0].Name);
    Assert.Equal(file0, files[0].Bytes);

    Assert.Equal("b.bin", files[1].Name);
    Assert.Equal(file1, files[1].Bytes);
  }

  private static byte[] Build7z_TwoFiles_TwoFolders_TwoPackStreams(
    string file0Name,
    byte[] file0Bytes,
    string file1Name,
    byte[] file1Bytes,
    int packPosPrefixLength,
    int dictionarySize,
    int maxUnpackChunkSize)
  {
    // PackStream #0: Copy (сырой поток)
    byte[] pack0 = file0Bytes;

    // PackStream #1: LZMA2(LZMA literal-only), chunked
    Assert.True(Lzma2Properties.TryEncode(dictionarySize, out byte lzma2PropsByte));
    var lzmaProps = new LzmaProperties(Lc: 3, Lp: 0, Pb: 2);

    byte[] pack1 = Lzma2LzmaEncoder.EncodeLiteralOnlyChunked(
      data: file1Bytes,
      lzmaProperties: lzmaProps,
      dictionarySize: dictionarySize,
      maxUnpackChunkSize: maxUnpackChunkSize,
      out _);

    // Делам PackPos != 0: в packed area будет префикс, потом pack0, потом pack1.
    byte[] prefix = new byte[packPosPrefixLength];
    for (int i = 0; i < prefix.Length; i++)
      prefix[i] = (byte)(0xA0 + i);

    byte[] nextHeader = BuildNextHeader_TwoFiles_TwoFolders(
      packPos: prefix.Length,
      pack0Size: pack0.Length,
      pack1Size: pack1.Length,
      file0Name: file0Name,
      file0Size: file0Bytes.Length,
      file1Name: file1Name,
      file1Size: file1Bytes.Length,
      lzma2PropsByte: lzma2PropsByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    int packedAreaSize = prefix.Length + pack0.Length + pack1.Length;

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedAreaSize,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedAreaSize + nextHeader.Length];
    sig.Write(archive);

    int o = SevenZipSignatureHeader.Size;

    Buffer.BlockCopy(prefix, 0, archive, o, prefix.Length);
    o += prefix.Length;

    Buffer.BlockCopy(pack0, 0, archive, o, pack0.Length);
    o += pack0.Length;

    Buffer.BlockCopy(pack1, 0, archive, o, pack1.Length);
    o += pack1.Length;

    Buffer.BlockCopy(nextHeader, 0, archive, o, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeader_TwoFiles_TwoFolders(
    int packPos,
    int pack0Size,
    int pack1Size,
    string file0Name,
    int file0Size,
    string file1Name,
    int file1Size,
    byte lzma2PropsByte)
  {
    List<byte> h = new(512)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo
    };
    WriteU64(h, (ulong)packPos);
    WriteU64(h, 2); // NumPackStreams = 2
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)pack0Size);
    WriteU64(h, (ulong)pack1Size);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 2); // NumFolders = 2
    h.Add(0x00);    // External = 0

    // ---- Folder #0: Copy (0x00)
    WriteU64(h, 1);     // NumCoders = 1
    h.Add(0x01);        // mainByte: idSize=1, без props
    h.Add(0x00);        // methodId: Copy
    // bindPairs=0, numPackedStreams=1 => packedStreamIndex не пишется

    // ---- Folder #1: LZMA2 (0x21) + props[1]
    WriteU64(h, 1);     // NumCoders = 1
    h.Add(0x21);        // mainByte: idSize=1 + hasProps
    h.Add(0x21);        // methodId: LZMA2
    WriteU64(h, 1);     // props size
    h.Add(lzma2PropsByte);
    // bindPairs=0, numPackedStreams=1 => packedStreamIndex не пишется

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)file0Size); // Folder #0 unpack size
    WriteU64(h, (ulong)file1Size); // Folder #1 unpack size
    h.Add(SevenZipNid.End);         // End UnpackInfo

    h.Add(SevenZipNid.End);         // End MainStreamsInfo (StreamsInfo)

    // FilesInfo (2 файла)
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2);

    h.Add(SevenZipNid.Name);
    byte[] namesBytes = Encoding.Unicode.GetBytes(file0Name + "\0" + file1Name + "\0");
    WriteU64(h, (ulong)(1 + namesBytes.Length));
    h.Add(0x00); // External = 0
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
