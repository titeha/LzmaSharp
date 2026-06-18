using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderUnsupportedMethodTests
{
  [Fact]
  public void ArchiveReader_UnknownMethod_HeaderVisible_Ok()
  {
    byte[] archive = BuildArchiveSingleFileUnknownMethod(
        packedBytes: MakePattern(64, mul: 17, add: 3),
        fileName: "file.bin",
        methodId: [0x7F]);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Single(folder.Coders);
    Assert.Equal(new byte[] { 0x7F }, folder.Coders[0].MethodId);
  }

  [Fact]
  public void PublicDecodeApis_UnknownMethod_NotSupported()
  {
    byte[] archive = BuildArchiveSingleFileUnknownMethod(
        packedBytes: MakePattern(64, mul: 17, add: 3),
        fileName: "file.bin",
        methodId: [0x7F]);

    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r1);
    Assert.Equal(archive.Length, consumed1);
    Assert.Empty(files);

    SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files2);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r2);
    Assert.Empty(files2);

    SevenZipArchiveDecodeResult r3 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed3);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r3);
    Assert.Equal(archive.Length, consumed3);
    Assert.Empty(entries);

    SevenZipArchiveDecodeResult r4 = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int consumed4);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r4);
    Assert.Equal(archive.Length, consumed4);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void ExtractToDirectory_UnknownMethod_NotSupported_AndDoesNotCreateDestination()
  {
    byte[] archive = BuildArchiveSingleFileUnknownMethod(
        packedBytes: MakePattern(64, mul: 17, add: 3),
        fileName: "file.bin",
        methodId: [0x7F]);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderUnsupportedMethodTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.False(Directory.Exists(root));
    }
    finally
    {
      try
      {
        if (Directory.Exists(root))
          Directory.Delete(root, recursive: true);
      }
      catch
      {
      }
    }
  }

  private static byte[] BuildArchiveSingleFileUnknownMethod(byte[] packedBytes, string fileName, byte[] methodId)
  {
    byte[] nextHeader = BuildNextHeaderSingleFileUnknownMethod(
        packSize: packedBytes.Length,
        unpackSize: packedBytes.Length,
        fileName: fileName,
        methodId: methodId);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedBytes.Length,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + packedBytes.Length + nextHeader.Length];

    sig.Write(archive);
    Buffer.BlockCopy(packedBytes, 0, archive, SevenZipSignatureHeader.Size, packedBytes.Length);
    Buffer.BlockCopy(nextHeader, 0, archive, SevenZipSignatureHeader.Size + packedBytes.Length, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderSingleFileUnknownMethod(
      int packSize,
      int unpackSize,
      string fileName,
      byte[] methodId)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 1); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize);

    h.Add(SevenZipNid.End);

    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 1);   // NumFolders
    h.Add(0x00);      // External = 0

    // Один простой coder с неизвестным MethodId.
    WriteU64(h, 1);   // NumCoders
    h.Add((byte)(methodId.Length & 0x0F)); // mainByte: только idSize
    h.AddRange(methodId);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

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

  private static byte[] MakePattern(int length, int mul, int add)
  {
    byte[] bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));
    return bytes;
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
