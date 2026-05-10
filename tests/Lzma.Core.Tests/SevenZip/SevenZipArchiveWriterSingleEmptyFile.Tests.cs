using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterSingleEmptyFileTests
{
  [Fact]
  public void BuildSingleEmptyFileArchive_СоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
    [new SevenZipArchiveWriterEntry("empty.txt", []),],
    out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal("empty.txt", fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("")]
  [InlineData("dir/file.txt")]
  [InlineData("dir\\file.txt")]
  [InlineData("bad\0name.txt")]
  public void BuildSingleEmptyFileArchive_НекорректноеИмяВозвращаетInvalidData(string fileName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
      [new SevenZipArchiveWriterEntry(fileName, []),],
      out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildSingleEmptyFileArchive_NullИмяВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        null!,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }
}
