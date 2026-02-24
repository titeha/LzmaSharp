using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipLzmaSolidThreeFilesLastEmptyIntegrationTests
{
  [Fact]
  public void DecodeToArray_Lzma1Solid_ThreeFiles_LastEmptyStream_SplitBySubStreamsInfoSize_Ok()
  {
    const string name0 = "a.bin";
    const string name1 = "b.bin";
    const string name2 = "empty.bin";

    const int dictionarySize = 1 << 20;

    byte[] file0 = new byte[123];
    for (int i = 0; i < file0.Length; i++)
      file0[i] = (byte)(i * 17 + 3);

    byte[] file1 = new byte[77];
    for (int i = 0; i < file1.Length; i++)
      file1[i] = (byte)(i * 31 + 7);

    // Solid-поток содержит только НЕ-пустые файлы: file0 + file1.
    byte[] solid = new byte[file0.Length + file1.Length];
    Buffer.BlockCopy(file0, 0, solid, 0, file0.Length);
    Buffer.BlockCopy(file1, 0, solid, file0.Length, file1.Length);

    // LZMA1 raw stream (без LZMA-Alone header), literal-only.
    var lzmaProps = new LzmaProperties(3, 0, 2);
    byte lzmaPropsByte = lzmaProps.ToByteOrThrow();

    var enc = new LzmaEncoder(lzmaProps, dictionarySize);
    byte[] packedStream = enc.EncodeLiteralOnly(solid);

    byte[] archive = Build7z_SolidLzma1_ThreeFiles_LastEmpty(
      packedStream: packedStream,
      unpackSizeTotal: solid.Length,
      firstNonEmptySize: file0.Length,
      fileName0: name0,
      fileName1: name1,
      fileName2: name2,
      lzmaPropsByte: lzmaPropsByte,
      dictionarySize: dictionarySize);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(3, files.Length);

    Assert.Equal(name0, files[0].Name);
    Assert.Equal(file0, files[0].Bytes);

    Assert.Equal(name1, files[1].Name);
    Assert.Equal(file1, files[1].Bytes);

    Assert.Equal(name2, files[2].Name);
    Assert.Empty(files[2].Bytes);
  }

  private static byte[] Build7z_SolidLzma1_ThreeFiles_LastEmpty(
    byte[] packedStream,
    int unpackSizeTotal,
    int firstNonEmptySize,
    string fileName0,
    string fileName1,
    string fileName2,
    byte lzmaPropsByte,
    int dictionarySize)
  {
    byte[] nextHeader = BuildNextHeader_SolidLzma1_ThreeFiles_LastEmpty(
      packSize: packedStream.Length,
      unpackSizeTotal: unpackSizeTotal,
      firstNonEmptySize: firstNonEmptySize,
      fileName0: fileName0,
      fileName1: fileName1,
      fileName2: fileName2,
      lzmaPropsByte: lzmaPropsByte,
      dictionarySize: dictionarySize);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStream.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStream.Length + nextHeader.Length];
    sig.Write(archive);

    packedStream.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    nextHeader.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packedStream.Length));

    return archive;
  }

  private static byte[] BuildNextHeader_SolidLzma1_ThreeFiles_LastEmpty(
    int packSize,
    int unpackSizeTotal,
    int firstNonEmptySize,
    string fileName0,
    string fileName1,
    string fileName2,
    byte lzmaPropsByte,
    int dictionarySize)
  {
    if (firstNonEmptySize < 0 || firstNonEmptySize > unpackSizeTotal)
      throw new ArgumentOutOfRangeException(nameof(firstNonEmptySize));

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
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External = 0

    // Folder: 1 coder (LZMA)
    WriteU64(h, 1);

    // coder0: LZMA (7z) methodId = {03 01 01}, props = 5 bytes:
    // [0]=propsByte (lc/lp/pb), [1..4]=dict size (UInt32 LE).
    h.Add(0x23); // idSize=3 + hasProps
    h.Add(0x03);
    h.Add(0x01);
    h.Add(0x01);

    WriteU64(h, 5);
    h.Add(lzmaPropsByte);

    uint dictU32 = unchecked((uint)dictionarySize);
    h.Add((byte)(dictU32 & 0xFF));
    h.Add((byte)((dictU32 >> 8) & 0xFF));
    h.Add((byte)((dictU32 >> 16) & 0xFF));
    h.Add((byte)((dictU32 >> 24) & 0xFF));

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSizeTotal);

    h.Add(SevenZipNid.End); // End UnpackInfo

    // SubStreamsInfo: 2 sub-streams в одном folder (соответствуют двум НЕ-пустым файлам).
    h.Add(SevenZipNid.SubStreamsInfo);

    h.Add(SevenZipNid.NumUnpackStream);
    WriteU64(h, 2); // для единственного folder'а

    h.Add(SevenZipNid.Size);
    // Для streams=2 пишется только (streams-1)=1 размер.
    WriteU64(h, (ulong)firstNonEmptySize);

    h.Add(SevenZipNid.End); // End SubStreamsInfo

    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 3); // NumFiles

    // EmptyStream: 3 бита, последний файл пустой => 0010_0000 (MSB->LSB) => 0x20.
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x20);

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes($"{fileName0}\0{fileName1}\0{fileName2}\0");
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
