using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma1;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipLzmaCoderIntegrationTests
{
  [Fact]
  public void DecodeSingleFileToArray_Lzma1_Ok()
  {
    const string fileName = "lzma.bin";
    const int dictionarySize = 1 << 20;

    // Стандартные свойства LZMA SDK: lc=3 lp=0 pb=2 => props = 0x5D.
    var lzmaProps = new LzmaProperties(3, 0, 2);
    byte lzmaPropsByte = lzmaProps.ToByteOrThrow();

    // Данные делаем достаточно длинными, чтобы тест был “живой”.
    byte[] plain = new byte[257];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 31 + 7);

    // LZMA-поток без LZMA-Alone header: range-stream, который декодер читает по известному unpackSize.
    var enc = new LzmaEncoder(lzmaProps, dictionarySize);
    byte[] packedStream = enc.EncodeLiteralOnly(plain);

    byte[] archive = Build7z_SingleFile_SingleFolder_Lzma(
      packedStream: packedStream,
      unpackSize: plain.Length,
      fileName: fileName,
      lzmaPropsByte: lzmaPropsByte,
      dictionarySize: dictionarySize);

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

  private static byte[] Build7z_SingleFile_SingleFolder_Lzma(
    byte[] packedStream,
    int unpackSize,
    string fileName,
    byte lzmaPropsByte,
    int dictionarySize)
  {
    byte[] nextHeader = BuildNextHeader_SingleFile_Lzma(
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

  private static byte[] BuildNextHeader_SingleFile_Lzma(
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
    WriteU64(h, 1); // NumFolders
    h.Add(0x00);    // External = 0

    // Folder: 1 coder (LZMA)
    WriteU64(h, 1);

    // coder0: LZMA (7z) method id = { 03 01 01 }, properties = 5 bytes:
    // [0] = propsByte (lc/lp/pb), [1..4] = dictSize (UInt32 LE).
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

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

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
