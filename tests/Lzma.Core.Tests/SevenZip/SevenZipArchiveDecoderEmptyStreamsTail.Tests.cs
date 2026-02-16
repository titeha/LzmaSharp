using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderEmptyStreamsTailTests
{
  [Fact]
  public void DecodeAllFilesToArray_FirstHasData_SecondEmptyStream_Ok()
  {
    byte[] data = new byte[128];
    for (int i = 0; i < data.Length; i++)
      data[i] = (byte)(i * 17 + 3);

    byte[] archive = Build7z_TwoFiles_FirstData_SecondEmpty(
      fileName: "file.bin",
      emptyName: "empty",
      fileBytes: data,
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(2, files.Length);

    Assert.Equal("file.bin", files[0].Name);
    Assert.Equal(data, files[0].Bytes);

    Assert.Equal("empty", files[1].Name);
    Assert.Empty(files[1].Bytes);
  }

  private static byte[] Build7z_TwoFiles_FirstData_SecondEmpty(
    string fileName,
    string emptyName,
    ReadOnlySpan<byte> fileBytes,
    int dictionarySize)
  {
    byte[] packedStream = Lzma2CopyEncoder.Encode(fileBytes, dictionarySize, out byte lzma2PropsByte);

    byte[] nextHeader = BuildHeader_TwoFiles_FirstData_SecondEmpty(
      fileName: fileName,
      emptyName: emptyName,
      packSize: (ulong)packedStream.Length,
      unpackSize: (ulong)fileBytes.Length,
      lzma2PropertiesByte: lzma2PropsByte);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
      NextHeaderOffset: (ulong)packedStream.Length,
      NextHeaderSize: (ulong)nextHeader.Length,
      NextHeaderCrc: nextHeaderCrc);

    byte[] sigBytes = new byte[SevenZipSignatureHeader.TotalSize];
    sig.Write(sigBytes);

    byte[] archive = new byte[sigBytes.Length + packedStream.Length + nextHeader.Length];
    sigBytes.CopyTo(archive, 0);
    packedStream.CopyTo(archive.AsSpan(sigBytes.Length));
    nextHeader.CopyTo(archive.AsSpan(sigBytes.Length + packedStream.Length));

    return archive;
  }

  private static byte[] BuildHeader_TwoFiles_FirstData_SecondEmpty(
    string fileName,
    string emptyName,
    ulong packSize,
    ulong unpackSize,
    byte lzma2PropertiesByte)
  {
    List<byte> h = new(256)
    {
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,

      // PackInfo
      SevenZipNid.PackInfo,
    };

    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, packSize);
    h.Add(SevenZipNid.End);

    // UnpackInfo
    h.Add(SevenZipNid.UnpackInfo);

    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1); // NumFolders
    h.Add(0);       // External = 0

    // NumCoders
    WriteU64(h, 1);

    // Coder: LZMA2 (0x21) + properties size 1
    h.Add(0x21); // main byte: idSize=1 + hasProps
    h.Add(0x21); // method id
    WriteU64(h, 1);
    h.Add(lzma2PropertiesByte);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 2 files
    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2);

    // kEmptyStream: [false, true] => 0x40
    h.Add(SevenZipNid.EmptyStream);
    WriteU64(h, 1);
    h.Add(0x40);

    // kName
    h.Add(SevenZipNid.Name);
    byte[] namesBytes = Encoding.Unicode.GetBytes(fileName + "\0" + emptyName + "\0");
    WriteU64(h, (ulong)(1 + namesBytes.Length));
    h.Add(0); // External = 0
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
