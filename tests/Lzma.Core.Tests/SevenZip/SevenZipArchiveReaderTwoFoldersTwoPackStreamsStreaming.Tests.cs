using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderTwoFoldersTwoPackStreamsStreamingTests
{
  [Fact]
  public void Read_Потоково_TwoFolders_TwoPackStreams_PackPosNonZero_Ok_ИFoldersДекодируются()
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

    var reader = new SevenZipArchiveReader();

    int pos = 0;
    int patternIndex = 0;
    int[] chunkPattern = [1, 2, 3, 4, 5];

    SevenZipArchiveReadResult res = SevenZipArchiveReadResult.NeedMoreInput;

    while (pos < archive.Length)
    {
      int remaining = archive.Length - pos;
      int take = Math.Min(remaining, chunkPattern[patternIndex++ % chunkPattern.Length]);

      res = reader.Read(archive.AsSpan(pos, take), out int consumed);

      Assert.InRange(consumed, 0, take);
      pos += consumed;

      if (res == SevenZipArchiveReadResult.Ok)
        break;

      Assert.Equal(SevenZipArchiveReadResult.NeedMoreInput, res);

      // Контракт текущего reader’а: при NeedMoreInput он должен забирать весь кусок.
      Assert.Equal(take, consumed);
      Assert.True(consumed > 0);
    }

    Assert.Equal(SevenZipArchiveReadResult.Ok, res);
    Assert.Equal(archive.Length, pos);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;

    Assert.Equal(2UL, header.FilesInfo.FileCount);
    Assert.True(header.FilesInfo.HasNames);
    Assert.Equal("a.bin", header.FilesInfo.Names![0]);
    Assert.Equal("b.bin", header.FilesInfo.Names![1]);

    ReadOnlySpan<byte> packedStreams = reader.PackedStreams.Span;

    // Folder 0 = Copy => file0
    Assert.Equal(SevenZipFolderDecodeResult.Ok,
      SevenZipFolderDecoder.DecodeFolderToArray(header.StreamsInfo, packedStreams, folderIndex: 0, out byte[] out0));
    Assert.Equal(file0, out0);

    // Folder 1 = LZMA2(LZMA chunks) => file1
    Assert.Equal(SevenZipFolderDecodeResult.Ok,
      SevenZipFolderDecoder.DecodeFolderToArray(header.StreamsInfo, packedStreams, folderIndex: 1, out byte[] out1));
    Assert.Equal(file1, out1);

    // После Ok reader должен быть терминальным.
    ReadOnlySpan<byte> extra = stackalloc byte[3];
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(extra, out int extraConsumed));
    Assert.Equal(0, extraConsumed);
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

    // PackPos != 0: префикс, потом pack0, потом pack1
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
    WriteU64(h, 2); // NumPackStreams
    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)pack0Size);
    WriteU64(h, (ulong)pack1Size);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 2); // NumFolders
    h.Add(0x00);    // External = 0

    // Folder #0: Copy
    WriteU64(h, 1); // NumCoders
    h.Add(0x01);    // mainByte: idSize=1, без props
    h.Add(0x00);    // methodId: Copy

    // Folder #1: LZMA2 + props
    WriteU64(h, 1);   // NumCoders
    h.Add(0x21);      // mainByte: idSize=1 + hasProps
    h.Add(0x21);      // methodId: LZMA2
    WriteU64(h, 1);   // props size
    h.Add(lzma2PropsByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)file0Size);
    WriteU64(h, (ulong)file1Size);
    h.Add(SevenZipNid.End); // End UnpackInfo

    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
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
