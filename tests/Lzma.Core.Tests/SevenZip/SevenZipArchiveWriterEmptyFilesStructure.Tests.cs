using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterEmptyFilesStructureTests
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
}
