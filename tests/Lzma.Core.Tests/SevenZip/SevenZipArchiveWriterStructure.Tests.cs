using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterStructureTests
{
  [Fact]
  public void BuildArchive_ОдинНепустойCopyФайлФормируетОжидаемуюСтруктуруHeader()
  {
    byte[] content = [1, 2, 3, 4, 5];
    uint contentCrc = Crc32.Compute(content);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
      [new SevenZipArchiveWriterEntry("file.bin", content)],
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
  }

  private static void AssertPackInfo(
      SevenZipHeader header,
      int contentLength,
      uint contentCrc)
  {
    Assert.True(header.StreamsInfo.PackInfo.HasValue);

    SevenZipPackInfo packInfo = header.StreamsInfo.PackInfo.Value;

    Assert.Equal(0UL, packInfo.PackPos);

    ulong packSize = Assert.Single(packInfo.PackSizes);
    Assert.Equal((ulong)contentLength, packSize);

    Assert.True(packInfo.HasCrc);
    Assert.NotNull(packInfo.CrcDefined);
    Assert.NotNull(packInfo.Crc);

    bool crcDefined = Assert.Single(packInfo.CrcDefined);
    uint crc = Assert.Single(packInfo.Crc);

    Assert.True(crcDefined);
    Assert.Equal(contentCrc, crc);
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

    bool folderCrcDefined = Assert.Single(unpackInfo.FolderCrcDefined);
    uint folderCrc = Assert.Single(unpackInfo.FolderCrc);

    Assert.True(folderCrcDefined);
    Assert.Equal(contentCrc, folderCrc);
  }

  private static void AssertFilesInfo(SevenZipHeader header)
  {
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(1UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);

    string fileName = Assert.Single(filesInfo.Names);

    Assert.Equal("file.bin", fileName);

    Assert.False(filesInfo.HasEmptyStreams);
    Assert.False(filesInfo.HasEmptyFiles);

    // CRC файлов в FilesInfo не пишем: целостность покрыта folder-CRC в UnpackInfo.
    Assert.False(filesInfo.HasCrc);
    Assert.Null(filesInfo.CrcDefined);
    Assert.Null(filesInfo.Crc);
  }
}
