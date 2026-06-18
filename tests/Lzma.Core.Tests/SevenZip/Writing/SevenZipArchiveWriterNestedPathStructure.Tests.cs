using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterNestedPathStructureTests
{
  [Fact]
  public void BuildArchive_ВложенныеEntryPathФормируютОжидаемуюСтруктуруHeader()
  {
    byte[] content = [1, 2, 3, 4];
    uint contentCrc = Crc32.Compute(content);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
                new SevenZipArchiveWriterEntry("dir/empty.txt", []),
                new SevenZipArchiveWriterEntry("dir/file.bin", content),
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

    Assert.Equal((ulong)content.Length, signatureHeader.NextHeaderOffset);
    Assert.Equal((ulong)reader.NextHeaderBytes.Length, signatureHeader.NextHeaderSize);
    Assert.Equal(Crc32.Compute(reader.NextHeaderBytes.Span), signatureHeader.NextHeaderCrc);

    Assert.True(reader.NextHeaderKind.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind.Value);

    Assert.Equal(content, reader.PackedStreams.ToArray());

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;

    AssertPackInfo(header, content.Length, contentCrc);
    AssertUnpackInfo(header, content.Length, contentCrc);
    AssertFilesInfo(header);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyStream,
        expectedBytes: [0xC0]);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyFile,
        expectedBytes: [0x40]);
  }

  private static void AssertPackInfo(
      SevenZipHeader header,
      int contentLength,
      uint contentCrc)
  {
    Assert.True(header.StreamsInfo.PackInfo.HasValue);

    SevenZipPackInfo packInfo = header.StreamsInfo.PackInfo.Value;

    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Equal([(ulong)contentLength], packInfo.PackSizes);

    Assert.True(packInfo.HasCrc);
    Assert.NotNull(packInfo.CrcDefined);
    Assert.NotNull(packInfo.Crc);

    Assert.Equal([true], packInfo.CrcDefined!);
    Assert.Equal([contentCrc], packInfo.Crc!);
  }

  private static void AssertUnpackInfo(
      SevenZipHeader header,
      int contentLength,
      uint contentCrc)
  {
    Assert.NotNull(header.StreamsInfo.UnpackInfo);

    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;

    SevenZipFolder folder = Assert.Single(unpackInfo.Folders);

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

    ulong[] folderUnpackSizes = Assert.Single(unpackInfo.FolderUnpackSizes);
    ulong unpackSize = Assert.Single(folderUnpackSizes);

    Assert.Equal((ulong)contentLength, unpackSize);

    Assert.True(unpackInfo.HasFolderCrcDefined);
    Assert.True(unpackInfo.HasFolderCrc);
    Assert.NotNull(unpackInfo.FolderCrcDefined);
    Assert.NotNull(unpackInfo.FolderCrc);

    Assert.Equal([true], unpackInfo.FolderCrcDefined!);
    Assert.Equal([contentCrc], unpackInfo.FolderCrc!);
  }

  private static void AssertFilesInfo(SevenZipHeader header)
  {
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(3UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(["dir", "dir/empty.txt", "dir/file.bin"], filesInfo.Names!);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal([true, true, false], filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);
    Assert.Equal([false, true, false], filesInfo.EmptyFiles!);

    // CRC файлов в FilesInfo не пишем: целостность покрыта folder-CRC в UnpackInfo.
    Assert.False(filesInfo.HasCrc);
    Assert.Null(filesInfo.CrcDefined);
    Assert.Null(filesInfo.Crc);
  }

  private static void AssertBitVectorProperty(
      ReadOnlySpan<byte> nextHeader,
      byte propertyId,
      byte[] expectedBytes)
  {
    int propertyOffset = FindPropertyOffset(nextHeader, propertyId);

    Assert.True(propertyOffset >= 0);

    Assert.Equal((byte)expectedBytes.Length, nextHeader[propertyOffset + 1]);

    ReadOnlySpan<byte> actualBytes = nextHeader.Slice(
        propertyOffset + 2,
        expectedBytes.Length);

    Assert.Equal(expectedBytes, actualBytes.ToArray());
  }

  private static int FindPropertyOffset(
      ReadOnlySpan<byte> nextHeader,
      byte propertyId)
  {
    for (int i = 0; i < nextHeader.Length; i++)
      if (nextHeader[i] == propertyId)
        return i;

    return -1;
  }

}
