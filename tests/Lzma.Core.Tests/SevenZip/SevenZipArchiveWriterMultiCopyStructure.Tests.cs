using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterMultiCopyStructureTests
{
  [Fact]
  public void BuildArchive_НесколькоНепустыхCopyФайловФормируетОжидаемуюСтруктуруHeader()
  {
    byte[] firstContent = [1, 2, 3];
    byte[] secondContent = [4, 5, 6, 7];

    uint firstCrc = Crc32.Compute(firstContent);
    uint secondCrc = Crc32.Compute(secondContent);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.bin", firstContent),
                new SevenZipArchiveWriterEntry("b.bin", secondContent),
            ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult readResult = reader.Read(
        archive,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, readResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.SignatureHeader.HasValue);

    SevenZipSignatureHeader signatureHeader = reader.SignatureHeader.Value;

    int packedDataLength = firstContent.Length + secondContent.Length;

    Assert.Equal((ulong)packedDataLength, signatureHeader.NextHeaderOffset);
    Assert.Equal((ulong)reader.NextHeaderBytes.Length, signatureHeader.NextHeaderSize);
    Assert.Equal(Crc32.Compute(reader.NextHeaderBytes.Span), signatureHeader.NextHeaderCrc);

    Assert.True(reader.NextHeaderKind.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind.Value);

    Assert.Equal(
        [1, 2, 3, 4, 5, 6, 7],
        reader.PackedStreams.ToArray());

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;

    AssertPackInfo(header, firstContent.Length, secondContent.Length, firstCrc, secondCrc);
    AssertUnpackInfo(header, firstContent.Length, secondContent.Length, firstCrc, secondCrc);
    AssertFilesInfo(header, firstCrc, secondCrc);
  }

  private static void AssertPackInfo(
      SevenZipHeader header,
      int firstLength,
      int secondLength,
      uint firstCrc,
      uint secondCrc)
  {
    Assert.True(header.StreamsInfo.PackInfo.HasValue);

    SevenZipPackInfo packInfo = header.StreamsInfo.PackInfo.Value;

    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Equal([(ulong)firstLength, (ulong)secondLength], packInfo.PackSizes);

    Assert.True(packInfo.HasCrc);
    Assert.NotNull(packInfo.CrcDefined);
    Assert.NotNull(packInfo.Crc);

    Assert.Equal([true, true], packInfo.CrcDefined!);
    Assert.Equal([firstCrc, secondCrc], packInfo.Crc!);
  }

  private static void AssertUnpackInfo(
      SevenZipHeader header,
      int firstLength,
      int secondLength,
      uint firstCrc,
      uint secondCrc)
  {
    Assert.NotNull(header.StreamsInfo.UnpackInfo);

    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;

    Assert.Equal(2, unpackInfo.Folders.Length);

    AssertCopyFolder(unpackInfo.Folders[0]);
    AssertCopyFolder(unpackInfo.Folders[1]);

    Assert.Equal(2, unpackInfo.FolderUnpackSizes.Length);
    Assert.Equal([(ulong)firstLength], unpackInfo.FolderUnpackSizes[0]);
    Assert.Equal([(ulong)secondLength], unpackInfo.FolderUnpackSizes[1]);

    Assert.True(unpackInfo.HasFolderCrcDefined);
    Assert.True(unpackInfo.HasFolderCrc);
    Assert.NotNull(unpackInfo.FolderCrcDefined);
    Assert.NotNull(unpackInfo.FolderCrc);

    Assert.Equal([true, true], unpackInfo.FolderCrcDefined!);
    Assert.Equal([firstCrc, secondCrc], unpackInfo.FolderCrc!);
  }

  private static void AssertCopyFolder(SevenZipFolder folder)
  {
    Assert.Equal(1UL, folder.NumInStreams);
    Assert.Equal(1UL, folder.NumOutStreams);
    Assert.Empty(folder.BindPairs);

    ulong packedStreamIndex = Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(0UL, packedStreamIndex);

    SevenZipCoderInfo coder = Assert.Single(folder.Coders);

    Assert.Equal([0x00], coder.MethodId);
    Assert.Empty(coder.Properties);
    Assert.Equal(1UL, coder.NumInStreams);
    Assert.Equal(1UL, coder.NumOutStreams);
  }

  private static void AssertFilesInfo(
      SevenZipHeader header,
      uint firstCrc,
      uint secondCrc)
  {
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(2UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(["a.bin", "b.bin"], filesInfo.Names!);

    Assert.False(filesInfo.HasEmptyStreams);
    Assert.Null(filesInfo.EmptyStreams);

    Assert.False(filesInfo.HasEmptyFiles);
    Assert.Null(filesInfo.EmptyFiles);

    Assert.True(filesInfo.HasCrc);
    Assert.NotNull(filesInfo.CrcDefined);
    Assert.NotNull(filesInfo.Crc);

    Assert.Equal([true, true], filesInfo.CrcDefined!);
    Assert.Equal([firstCrc, secondCrc], filesInfo.Crc!);
  }
}
