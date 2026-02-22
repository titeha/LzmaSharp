using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipSwapFiltersChainedCodersIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_Swap2ThenLzma2_Ok()
  {
    byte[] plain = new byte[67]; // не кратно 2 => проверяем хвост
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    byte[] swapEncoded = Swap2Transform(plain);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_SwapThenLzma2(
      packedPayload: swapEncoded,
      fileName: "swap2.bin",
      methodId: [0x02, 0x03, 0x02], // Swap2
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("swap2.bin", decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_Swap4ThenLzma2_Ok()
  {
    byte[] plain = new byte[67]; // не кратно 4 => проверяем хвост
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 17 + 3);

    byte[] swapEncoded = Swap4Transform(plain);

    byte[] archive = Build7z_SingleFile_SingleFolder_TwoCoders_SwapThenLzma2(
      packedPayload: swapEncoded,
      fileName: "swap4.bin",
      methodId: [0x02, 0x03, 0x04], // Swap4
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeSingleFileToArray(
      archive,
      out byte[] decodedBytes,
      out string decodedName,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("swap4.bin", decodedName);
    Assert.Equal(plain, decodedBytes);
  }

  private static byte[] Build7z_SingleFile_SingleFolder_TwoCoders_SwapThenLzma2(
    byte[] packedPayload,
    string fileName,
    byte[] methodId,
    int dictionarySize)
  {
    // Сжимаем (фактически просто упаковываем) данные через LZMA2 COPY.
    byte[] packedStream = Lzma2CopyEncoder.Encode(packedPayload, dictionarySize, out byte lzma2PropsByte);

    byte[] nextHeader = BuildNextHeader_SingleFile_TwoCoders_SwapThenLzma2(
      packSize: packedStream.Length,
      unpackSize: packedPayload.Length,
      fileName: fileName,
      swapMethodId: methodId,
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

  private static byte[] BuildNextHeader_SingleFile_TwoCoders_SwapThenLzma2(
    int packSize,
    int unpackSize,
    string fileName,
    byte[] swapMethodId,
    byte lzma2PropsByte)
  {
    Assert.Equal(3, swapMethodId.Length);

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

    // coder0: Swap2/Swap4 (idSize=3, без props)
    h.Add(0x03); // mainByte: idSize=3
    h.Add(swapMethodId[0]);
    h.Add(swapMethodId[1]);
    h.Add(swapMethodId[2]);

    // coder1: LZMA2 (idSize=1 + props)
    h.Add(0x21); // mainByte
    h.Add(0x21); // methodId: LZMA2
    WriteU64(h, 1);
    h.Add(lzma2PropsByte);

    // BindPairs: InIndex(0) <- OutIndex(1)
    // Вход Swap связан с выходом LZMA2 => packed stream идёт во вход LZMA2 (InIndex=1).
    WriteU64(h, 0);
    WriteU64(h, 1);

    h.Add(SevenZipNid.CodersUnpackSize);
    // out0 (Swap final)
    WriteU64(h, (ulong)unpackSize);
    // out1 (LZMA2 intermediate)
    WriteU64(h, (ulong)unpackSize);

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

  private static byte[] Swap2Transform(byte[] src)
  {
    byte[] dst = (byte[])src.Clone();

    for (int i = 0; i + 2 <= dst.Length; i += 2)
      (dst[i + 1], dst[i]) = (dst[i], dst[i + 1]);

    return dst;
  }

  private static byte[] Swap4Transform(byte[] src)
  {
    byte[] dst = (byte[])src.Clone();

    for (int i = 0; i + 4 <= dst.Length; i += 4)
    {
      (dst[i], dst[i + 3]) = (dst[i + 3], dst[i]);
      (dst[i + 1], dst[i + 2]) = (dst[i + 2], dst[i + 1]);
    }

    return dst;
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
