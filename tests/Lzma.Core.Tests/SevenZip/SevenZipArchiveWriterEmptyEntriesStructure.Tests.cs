using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterEmptyEntriesStructureTests
{
  [Fact]
  public void BuildArchive_НесколькоПустыхФайловФормируетОжидаемыйFilesInfo()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
                new SevenZipArchiveWriterFile("a.txt", []),
                new SevenZipArchiveWriterFile("b.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipHeader header = ReadHeaderForArchiveWithoutPackedData(archive);

    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(2UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(new[] { "a.txt", "b.txt" }, filesInfo.Names!);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal([true, true], filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);
    Assert.Equal([true, true], filesInfo.EmptyFiles!);

    Assert.False(filesInfo.HasAnti);
    Assert.Null(filesInfo.Anti);

    Assert.False(filesInfo.HasCrc);
    Assert.Null(filesInfo.CrcDefined);
    Assert.Null(filesInfo.Crc);
  }

  [Fact]
  public void BuildArchive_ПустойФайлИДиректорияФормируютОжидаемыйEmptyFileBitVector()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
                new SevenZipArchiveWriterFile("a.txt", []),
                new SevenZipArchiveWriterFile("dir", [], IsDirectory: true),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipHeader header = ReadHeaderForArchiveWithoutPackedData(archive);

    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(2UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(new[] { "a.txt", "dir" }, filesInfo.Names!);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal(new[] { true, true }, filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);

    // EmptyFile=true означает пустой файл.
    // EmptyFile=false при EmptyStream=true означает директорию.
    Assert.Equal(new[] { true, false }, filesInfo.EmptyFiles!);

    Assert.False(filesInfo.HasAnti);
    Assert.Null(filesInfo.Anti);

    Assert.False(filesInfo.HasCrc);
    Assert.Null(filesInfo.CrcDefined);
    Assert.Null(filesInfo.Crc);
  }

  private static SevenZipHeader ReadHeaderForArchiveWithoutPackedData(byte[] archive)
  {
    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult readResult = reader.Read(
        archive,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, readResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(reader.SignatureHeader.HasValue);

    SevenZipSignatureHeader signatureHeader = reader.SignatureHeader.Value;

    Assert.Equal(0UL, signatureHeader.NextHeaderOffset);
    Assert.Equal((ulong)reader.NextHeaderBytes.Length, signatureHeader.NextHeaderSize);
    Assert.Equal(Crc32.Compute(reader.NextHeaderBytes.Span), signatureHeader.NextHeaderCrc);

    Assert.True(reader.NextHeaderKind.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind.Value);

    Assert.Empty(reader.PackedStreams.ToArray());

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;

    Assert.Null((object?)header.StreamsInfo);

    return header;
  }
}
