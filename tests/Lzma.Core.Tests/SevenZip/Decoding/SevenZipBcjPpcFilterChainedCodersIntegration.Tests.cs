using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipBcjPpcFilterChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_BcjPpcThenLzma2_Ok()
  {
    const string fileName = "ppc.bin";
    const int dictionarySize = 1 << 20;

    // Делаем буфер не кратный 4, чтобы был хвост, который фильтр не трогает.
    byte[] plain = new byte[13];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    // PPC branch-инструкция на смещении 4 (кратно 4, чтобы конвертер её увидел):
    // data[i] >> 2 == 0x12 => 0x48..0x4B
    // data[i+3] & 3 == 1
    plain[4] = 0x48;
    plain[5] = 0x00;
    plain[6] = 0x10;
    plain[7] = 0x01;

    byte[] encoded = PpcEncodeTransform(plain, startOffset: 0);

    // Проверяем, что transform реально меняет данные.
    Assert.NotEqual(plain[7], encoded[7]);

    // Packed stream: LZMA2(COPY) от encoded.
    byte[] packedStream = Lzma2CopyEncoder.Encode(encoded, dictionarySize, out byte lzma2PropsByte);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_PpcThenLzma2(
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

  private static byte[] PpcEncodeTransform(byte[] src, uint startOffset)
  {
    // Инверсия к decode: PPC_Convert(..., encoding=1) из LZMA SDK (Bra.c).
    byte[] dst = (byte[])src.Clone();

    if (dst.Length < 4)
      return dst;

    int limit = dst.Length - 4;

    for (int i = 0; i <= limit; i += 4)
    {
      if ((dst[i] >> 2) != 0x12 || (dst[i + 3] & 3) != 1)
        continue;

      uint srcVal =
        ((uint)(dst[i + 0] & 3) << 24) |
        ((uint)dst[i + 1] << 16) |
        ((uint)dst[i + 2] << 8) |
        ((uint)dst[i + 3] & 0xFFFFFFFCu);

      uint dest = unchecked(startOffset + (uint)i + srcVal);

      dst[i + 0] = (byte)(0x48 | ((dest >> 24) & 0x3));
      dst[i + 1] = (byte)(dest >> 16);
      dst[i + 2] = (byte)(dest >> 8);

      byte b3 = dst[i + 3];
      b3 &= 0x3;
      b3 |= (byte)dest;
      dst[i + 3] = b3;
    }

    return dst;
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_PpcThenLzma2(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzma2PropsByte)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_PpcThenLzma2(
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_PpcThenLzma2(
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

    // coder0: PPC (Branch codec) methodId = {03 03 02 05}
    h.Add(0x04); // mainByte: idSize=4, без props
    h.Add(0x03);
    h.Add(0x03);
    h.Add(0x02);
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
    WriteU64(h, (ulong)unpackSize); // out0 (PPC final)
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
