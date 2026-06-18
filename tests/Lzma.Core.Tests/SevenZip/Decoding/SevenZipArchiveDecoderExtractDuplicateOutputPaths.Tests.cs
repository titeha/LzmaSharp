using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractDuplicateOutputPathsTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ExtractToDirectory_TwoEntries_WithSameOutputPath_InvalidData_AndNothingExtracted(bool overwrite)
  {
    byte[] bytes1 = MakePattern(64, mul: 17, add: 3);
    byte[] bytes2 = MakePattern(64, mul: 29, add: 5);

    byte[] archive = BuildArchiveTwoFilesCopy(
        fileName1: "dup.bin",
        fileBytes1: bytes1,
        fileName2: "dup.bin",
        fileBytes2: bytes2);

    // Сам архив как контейнер корректен.
    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);

    Assert.Equal(2, entries.Length);
    Assert.Equal("dup.bin", entries[0].Name);
    Assert.Equal("dup.bin", entries[1].Name);
    Assert.Equal(bytes1, entries[0].Bytes);
    Assert.Equal(bytes2, entries[1].Bytes);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderExtractDuplicateOutputPathsTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: overwrite,
          out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
      Assert.Equal(archive.Length, consumed2);

      // После предвалидации коллизий root уже может быть создан,
      // но внутрь ничего попадать не должно.
      Assert.True(Directory.Exists(root));
      Assert.Empty(Directory.GetFileSystemEntries(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] BuildArchiveTwoFilesCopy(
      string fileName1,
      byte[] fileBytes1,
      string fileName2,
      byte[] fileBytes2)
  {
    byte[] nextHeader = BuildNextHeaderTwoFilesCopy(
        packSize1: fileBytes1.Length,
        packSize2: fileBytes2.Length,
        unpackSize1: fileBytes1.Length,
        unpackSize2: fileBytes2.Length,
        fileName1: fileName1,
        fileName2: fileName2);

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var sig = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)(fileBytes1.Length + fileBytes2.Length),
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    byte[] archive = new byte[SevenZipSignatureHeader.Size + fileBytes1.Length + fileBytes2.Length + nextHeader.Length];

    sig.Write(archive);

    int pos = SevenZipSignatureHeader.Size;
    Buffer.BlockCopy(fileBytes1, 0, archive, pos, fileBytes1.Length);
    pos += fileBytes1.Length;

    Buffer.BlockCopy(fileBytes2, 0, archive, pos, fileBytes2.Length);
    pos += fileBytes2.Length;

    Buffer.BlockCopy(nextHeader, 0, archive, pos, nextHeader.Length);

    return archive;
  }

  private static byte[] BuildNextHeaderTwoFilesCopy(
      int packSize1,
      int packSize2,
      int unpackSize1,
      int unpackSize2,
      string fileName1,
      string fileName2)
  {
    List<byte> h =
    [
        SevenZipNid.Header,
            SevenZipNid.MainStreamsInfo,

            SevenZipNid.PackInfo,
        ];

    WriteU64(h, 0); // PackPos
    WriteU64(h, 2); // NumPackStreams

    h.Add(SevenZipNid.Size);
    WriteU64(h, (ulong)packSize1);
    WriteU64(h, (ulong)packSize2);

    h.Add(SevenZipNid.End);

    h.Add(SevenZipNid.UnpackInfo);
    h.Add(SevenZipNid.Folder);
    WriteU64(h, 2);   // NumFolders
    h.Add(0x00);      // External = 0

    WriteCopyFolder(h);
    WriteCopyFolder(h);

    h.Add(SevenZipNid.CodersUnpackSize);
    WriteU64(h, (ulong)unpackSize1);
    WriteU64(h, (ulong)unpackSize2);

    h.Add(SevenZipNid.End); // End UnpackInfo
    h.Add(SevenZipNid.End); // End StreamsInfo

    h.Add(SevenZipNid.FilesInfo);
    WriteU64(h, 2); // NumFiles

    h.Add(SevenZipNid.Name);

    byte[] nameBytes = Encoding.Unicode.GetBytes(fileName1 + "\0" + fileName2 + "\0");
    WriteU64(h, (ulong)(1 + nameBytes.Length));
    h.Add(0x00); // External = 0
    h.AddRange(nameBytes);

    h.Add(SevenZipNid.End); // End FilesInfo
    h.Add(SevenZipNid.End); // End Header

    return [.. h];
  }

  private static void WriteCopyFolder(List<byte> h)
  {
    WriteU64(h, 1);   // NumCoders
    h.Add(0x01);      // mainByte: idSize=1, простой coder
    h.Add(0x00);      // MethodId = Copy
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

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (!Directory.Exists(root))
        return;

      foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        File.SetAttributes(filePath, FileAttributes.Normal);

      string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
      Array.Sort(dirs, static (a, b) => b.Length.CompareTo(a.Length));

      foreach (string dirPath in dirs)
        File.SetAttributes(dirPath, FileAttributes.Directory);

      File.SetAttributes(root, FileAttributes.Directory);
    }
    catch
    {
    }

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
