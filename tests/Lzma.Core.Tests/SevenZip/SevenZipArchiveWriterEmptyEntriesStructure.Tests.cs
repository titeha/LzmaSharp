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
    Assert.Equal([true, true], filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);

    // EmptyFile=true означает пустой файл.
    // EmptyFile=false при EmptyStream=true означает директорию.
    Assert.Equal([true, false], filesInfo.EmptyFiles!);

    Assert.False(filesInfo.HasAnti);
    Assert.Null(filesInfo.Anti);

    Assert.False(filesInfo.HasCrc);
    Assert.Null(filesInfo.CrcDefined);
    Assert.Null(filesInfo.Crc);
  }

  [Fact]
  public void BuildArchive_ДевятьEmptyEntriesФормируютОжидаемыеBitVector()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterFile("f1.txt", []),
            new SevenZipArchiveWriterFile("f2.txt", []),
            new SevenZipArchiveWriterFile("f3.txt", []),
            new SevenZipArchiveWriterFile("f4.txt", []),
            new SevenZipArchiveWriterFile("f5.txt", []),
            new SevenZipArchiveWriterFile("f6.txt", []),
            new SevenZipArchiveWriterFile("f7.txt", []),
            new SevenZipArchiveWriterFile("f8.txt", []),
            new SevenZipArchiveWriterFile("dir", [], IsDirectory: true),
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

    Assert.True(reader.Header.HasValue);

    SevenZipFilesInfo filesInfo = reader.Header.Value.FilesInfo;

    Assert.Equal(9UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(
        new[]
        {
            "f1.txt",
            "f2.txt",
            "f3.txt",
            "f4.txt",
            "f5.txt",
            "f6.txt",
            "f7.txt",
            "f8.txt",
            "dir",
        },
        filesInfo.Names!);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal(
        [
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
        ],
        filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);
    Assert.Equal(
        [
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            false,
        ],
        filesInfo.EmptyFiles!);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyStream,
        expectedBytes: [0xFF, 0x80]);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyFile,
        expectedBytes: [0xFF, 0x00]);
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

  private static void AssertBitVectorProperty(
    ReadOnlySpan<byte> nextHeader,
    byte propertyId,
    byte[] expectedBytes)
  {
    int propertyOffset = FindPropertyOffset(nextHeader, propertyId);

    Assert.True(propertyOffset >= 0);

    Assert.Equal((byte)expectedBytes.Length, nextHeader[propertyOffset + 1]);

    ReadOnlySpan<byte> actualBytes = nextHeader.Slice(propertyOffset + 2, expectedBytes.Length);

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
