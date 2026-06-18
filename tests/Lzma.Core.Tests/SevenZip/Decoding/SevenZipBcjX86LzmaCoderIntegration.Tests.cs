using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjX86LzmaCoderIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjX86ThenLzma1_Ok()
  {
    const string fileName = "exe.bin";
    const int dictionarySize = 1 << 20;

    // "Оригинальные" байты (как в коде): E8 + rel32.
    // CALL из позиции 0 на цель 0x20 => rel = 0x20 - (0 + 5) = 0x1B.
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = 0x90; // NOP

    plain[0] = 0xE8;
    BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(1, 4), 0x20 - 5);

    // То, что хранится "после BCJ encode": abs = target = 0x20.
    byte[] bcjEncoded = (byte[])plain.Clone();
    BinaryPrimitives.WriteInt32LittleEndian(bcjEncoded.AsSpan(1, 4), 0x20);

    Assert.False(bcjEncoded.AsSpan().SequenceEqual(plain));

    // LZMA1 stream (raw, без LZMA-Alone header), literal-only.
    var lzmaProps = new LzmaProperties(3, 0, 2);
    byte lzmaPropsByte = lzmaProps.ToByteOrThrow();

    var enc = new LzmaEncoder(lzmaProps, dictionarySize);
    byte[] packedStream = enc.EncodeLiteralOnly(bcjEncoded);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_BcjX86ThenLzma1(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
      lzmaPropsByte: lzmaPropsByte,
      dictionarySize: dictionarySize);

    // Дополнительно фиксируем, что PackedStreamIndices вычисляется как 1 (поток идёт в LZMA coder).
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipFolder folder = header.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(1UL, folder.PackedStreamIndices[0]);

    // End-to-end decode
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

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_BcjX86ThenLzma1(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzmaPropsByte,
    int dictionarySize)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_BcjX86ThenLzma1(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_BcjX86ThenLzma1(
    int packSize,
    int unpackSize,
    string fileName,
    byte lzmaPropsByte,
    int dictionarySize)
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
    WriteU64(h, (ulong)packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);  // NumFolders
    h.Add(0x00);     // External = 0

    // Folder: 2 coders
    WriteU64(h, 2);

    // coder0: BCJ x86 methodId = {03 03 01 03}, props обычно нет
    h.Add(0x04); // mainByte: idSize=4
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x01);
    h.Add(0x03);

    // coder1: LZMA (7z) methodId = {03 01 01}, props=5 байт
    h.Add(0x23); // mainByte: idSize=3 + hasProps
    h.Add(0x03);
    h.Add(0x01);
    h.Add(0x01);

    WriteU64(h, 5); // props size
    h.Add(lzmaPropsByte);

    uint dictU32 = unchecked((uint)dictionarySize);
    h.Add((byte)(dictU32 & 0xFF));
    h.Add((byte)((dictU32 >> 8) & 0xFF));
    h.Add((byte)((dictU32 >> 16) & 0xFF));
    h.Add((byte)((dictU32 >> 24) & 0xFF));

    // BindPairs: InIndex(0) <- OutIndex(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    // NumPackedStreams = 2 - 1 = 1 => PackedStreamIndices не пишутся, должны вычисляться.

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize); // out0 (BCJ final)
    WriteU64(h, (ulong)unpackSize); // out1 (LZMA intermediate)

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1);

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
