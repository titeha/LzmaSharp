using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjArmtFilterChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjArmtThenLzma2_Ok()
  {
    const string fileName = "armt.bin";
    const int dictionarySize = 1 << 20;

    // Базовый буфер: не кратный 4, чтобы был хвост, который фильтр не трогает.
    byte[] plain = new byte[15];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 5);

    // Вставим Thumb-2 BL шаблон (условие из Bra.c):
    // (b1 & 0xF8) == 0xF0 и (b3 & 0xF8) == 0xF8
    // Выравнивание по 2 байтам.
    plain[0] = 0x34;
    plain[1] = 0xF2; // 0xF0 | 0x2
    plain[2] = 0x78;
    plain[3] = 0xFD; // 0xF8 | 0x5

    // ещё один BL на смещении 4
    plain[4] = 0x12;
    plain[5] = 0xF7;
    plain[6] = 0x9A;
    plain[7] = 0xFB;

    byte[] encoded = ArmtEncodeTransform(plain, startOffset: 0);

    // Проверяем, что трансформация реально меняет данные.
    Assert.False(encoded.AsSpan().SequenceEqual(plain));

    // LZMA2(COPY) от encoded.
    byte[] packedStream = Lzma2CopyEncoder.Encode(encoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_ArmtThenLzma2(
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

  private static byte[] ArmtEncodeTransform(byte[] src, uint startOffset)
  {
    // Инверсия к decode: ARMT_Convert(..., encoding=1) из Bra.c.
    byte[] dst = (byte[])src.Clone();

    if (dst.Length < 4)
      return dst;

    int limit = dst.Length - 4;
    uint ip = unchecked(startOffset + 4u);

    for (int i = 0; i <= limit; i += 2)
    {
      if ((dst[i + 1] & 0xF8) == 0xF0 &&
           (dst[i + 3] & 0xF8) == 0xF8)
      {
        uint srcVal =
          (((uint)dst[i + 1] & 0x7u) << 19) |
          ((uint)dst[i + 0] << 11) |
          (((uint)dst[i + 3] & 0x7u) << 8) |
          dst[i + 2];

        srcVal <<= 1;

        uint dest = unchecked(ip + (uint)i + srcVal);
        dest >>= 1;

        dst[i + 1] = (byte)(0xF0 | ((dest >> 19) & 0x7));
        dst[i + 0] = (byte)(dest >> 11);
        dst[i + 3] = (byte)(0xF8 | ((dest >> 8) & 0x7));
        dst[i + 2] = (byte)dest;

        i += 2;
      }
    }

    return dst;
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_ArmtThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_ArmtThenLzma2(
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_ArmtThenLzma2(
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

    // coder0: ARMT (Branch codec) methodId = {03 03 07 01}
    h.Add(0x04); // mainByte: idSize=4, без props
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x07);
    h.Add(0x01);

    // coder1: LZMA2
    h.Add(0x21);
    h.Add(0x21);
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize); // out0 (ARMT final)
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
