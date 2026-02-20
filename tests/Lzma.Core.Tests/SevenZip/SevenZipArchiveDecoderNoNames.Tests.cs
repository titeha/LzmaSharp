using Lzma.Core.Checksums;
using Lzma.Core.Lzma2;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderNoNamesTests
{
  [Fact]
  public void DecodeAllFilesToArray_ЕслиНетKName_ИспользуетFallbackИмя()
  {
    byte[] plain = new byte[64];
    for (int i = 0; i < plain.Length; i++)
      plain[i] = (byte)(i * 13 + 1);

    byte[] archive = Build7zArchive_SingleFile_Lzma2Copy_NoNames(
      plainFileBytes: plain,
      dictionarySize: 1 << 20);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Single(files);

    Assert.Equal("file_0", files[0].Name);
    Assert.Equal(plain, files[0].Bytes);
  }

  private static byte[] Build7zArchive_SingleFile_Lzma2Copy_NoNames(ReadOnlySpan<byte> plainFileBytes, int dictionarySize)
  {
    byte[] packedStream = Lzma2CopyEncoder.Encode(plainFileBytes, dictionarySize, out byte lzma2PropertiesByte);

    // ----- NextHeader ("Header") -----
    List<byte> headerPayload =
    [
      SevenZipNid.Header,
      SevenZipNid.MainStreamsInfo,
      SevenZipNid.PackInfo,
    ];

    WriteU64(headerPayload, 0); // PackPos
    WriteU64(headerPayload, 1); // NumPackStreams
    headerPayload.Add(SevenZipNid.Size);
    WriteU64(headerPayload, (ulong)packedStream.Length);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.UnpackInfo);

    headerPayload.Add(SevenZipNid.Folder);
    WriteU64(headerPayload, 1); // NumFolders
    headerPayload.Add(0);       // External = 0

    WriteU64(headerPayload, 1); // NumCoders

    headerPayload.Add(0b0010_0001); // MainByte: idSize=1, hasProps=1
    headerPayload.Add(0x21);        // MethodID: LZMA2
    headerPayload.Add(1);           // props size
    headerPayload.Add(lzma2PropertiesByte);

    headerPayload.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(headerPayload, (ulong)plainFileBytes.Length);
    headerPayload.Add(SevenZipNid.End); // End UnpackInfo

    headerPayload.Add(SevenZipNid.SubStreamsInfo);
    headerPayload.Add(SevenZipNid.NumUnpackStream);
    WriteU64(headerPayload, 1);
    headerPayload.Add(SevenZipNid.End);

    headerPayload.Add(SevenZipNid.End); // End MainStreamsInfo

    // FilesInfo: 1 файл, но без kName
    headerPayload.Add(SevenZipNid.FilesInfo);
    WriteU64(headerPayload, 1);       // NumFiles
    headerPayload.Add(SevenZipNid.End); // End FilesInfo properties

    headerPayload.Add(SevenZipNid.End); // End Header

    byte[] nextHeader = [.. headerPayload];
    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    // ----- SignatureHeader -----
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

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    var r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
