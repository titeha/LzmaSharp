using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjArmFilterChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjArmThenLzma2_Ok()
  {
    const string fileName = "arm.bin";
    const int dictionarySize = 1 << 20;

    // “Оригинальные” байты: один BL (последний байт 0xEB) на выровненном смещении.
    // Остальное — шум.
    byte[] plain = new byte[18]; // не кратно 4 => хвост должен остаться неизменным
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 5);

    // BL с произвольным 24-битным immediate (нам важна только обратимость трансформации).
    // little-endian: [imm0, imm1, imm2, 0xEB]
    BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(4, 4), 0xEB001234u);

    byte[] armEncoded = ArmEncodeTransform(plain, startOffset: 0);

    // Чтобы тест был “смысловым”: данные реально должны отличаться после encode.
    Assert.NotEqual(plain[4], armEncoded[4]);

    // Packed stream: LZMA2(COPY) от armEncoded.
    byte[] packedStream = Lzma2CopyEncoder.Encode(armEncoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_FilterThenLzma2(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
      filterMethodId: [0x03, 0x03, 0x05, 0x01], // 7z Branch Codecs / ARM (см. Methods.txt)
      lzma2PropsByte: lzma2PropsByte);

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

  private static byte[] ArmEncodeTransform(byte[] src, uint startOffset)
  {
    // Инверсия к decode: это ARM_Convert(..., encoding=1) из Bra.c.
    byte[] dst = (byte[])src.Clone();

    int size = dst.Length & ~3;
    uint ip = unchecked(startOffset + 4u);

    for (int i = 0; i + 4 <= size; i += 4)
    {
      if (dst[i + 3] != 0xEB)
        continue;

      uint v = BinaryPrimitives.ReadUInt32LittleEndian(dst.AsSpan(i, 4));

      v <<= 2;
      v = unchecked(v + ip + (uint)(i + 4));
      v >>= 2;

      v &= 0x00FFFFFF;
      v |= 0xEB000000;

      BinaryPrimitives.WriteUInt32LittleEndian(dst.AsSpan(i, 4), v);
    }

    return dst;
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_FilterThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte[] filterMethodId,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_FilterThenLzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
      filterMethodId: filterMethodId,
      lzma2PropsByte: lzma2PropsByte);

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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_FilterThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte[] filterMethodId,
    byte lzma2PropsByte)
  {
    Assert.True(filterMethodId.Length >= 1 && filterMethodId.Length <= 15);

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

    // Folder: 2 coders (filter + compression)
    WriteU64(h, 2);

    // coder0: filter (без props)
    h.Add((byte)filterMethodId.Length); // mainByte: idSize
    h.AddRange(filterMethodId);

    // coder1: LZMA2 (idSize=1 + props)
    h.Add(0x21); // mainByte
    h.Add(0x21); // methodId: LZMA2
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize); // out0 (filter final)
    WriteU64(h, (ulong)unpackSize); // out1 (lzma2 intermediate)

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

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
