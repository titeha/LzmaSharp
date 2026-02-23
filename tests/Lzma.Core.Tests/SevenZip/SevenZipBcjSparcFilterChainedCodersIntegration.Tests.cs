using System.Buffers.Binary;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjSparcFilterChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjSparcThenLzma2_Ok()
  {
    const string fileName = "sparc.bin";
    const int dictionarySize = 1 << 20;

    // Не кратно 4: хвост должен остаться неизменным.
    byte[] plain = new byte[19];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    // Вставим SPARC call-подобные инструкции (big-endian) на смещениях, кратных 4:
    // 0x40xxxxxx и b1 top-bits 00, чтобы сработало условие фильтра.
    BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(4, 4), 0x40000005u);
    BinaryPrimitives.WriteUInt32BigEndian(plain.AsSpan(8, 4), 0x40001234u);

    byte[] encoded = SparcEncodeTransform(plain, startOffset: 0);

    // Должны отличаться, иначе тест не проверяет фильтр.
    Assert.False(encoded.AsSpan().SequenceEqual(plain));

    // Packed stream: LZMA2(COPY) от encoded.
    byte[] packedStream = Lzma2CopyEncoder.Encode(encoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_SparcThenLzma2(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
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

  private static byte[] SparcEncodeTransform(byte[] src, uint startOffset)
  {
    // Инверсия к decode: SPARC_Convert(..., encoding=1) из LZMA SDK (Bra.c).
    byte[] dst = (byte[])src.Clone();
    int size = dst.Length & ~3;

    for (int i = 0; i + 4 <= size; i += 4)
    {
      byte b0 = dst[i];
      byte b1 = dst[i + 1];

      if (!((b0 == 0x40 && (b1 & 0xC0) == 0) || (b0 == 0x7F && b1 >= 0xC0)))
        continue;

      uint v = BinaryPrimitives.ReadUInt32BigEndian(dst.AsSpan(i, 4));

      v <<= 2;
      v = unchecked(v + (startOffset + (uint)i));

      v &= 0x01FFFFFFu;
      v = unchecked(v - (1u << 24));
      v ^= 0xFF000000u;
      v >>= 2;
      v |= 0x40000000u;

      BinaryPrimitives.WriteUInt32BigEndian(dst.AsSpan(i, 4), v);
    }

    return dst;
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_SparcThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_SparcThenLzma2(
      packSize: packedStream.Length,
      unpackSize: unpackSize,
      fileName: fileName,
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_SparcThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
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
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External=0

    // Folder: 2 coders (filter + compression)
    WriteU64(h, 2);

    // coder0: SPARC (Branch codec) methodId = {03 03 08 05}
    h.Add(0x04); // mainByte: idSize=4, без props
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x08);
    h.Add(0x05);

    // coder1: LZMA2
    h.Add(0x21);
    h.Add(0x21);
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize); // out0 (SPARC final)
    WriteU64(h, (ulong)unpackSize); // out1 (LZMA2 intermediate)

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 1);

    h.Add(SevenZipNid.Name);
    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00);
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End);
    h.Add(SevenZipNid.End);

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
