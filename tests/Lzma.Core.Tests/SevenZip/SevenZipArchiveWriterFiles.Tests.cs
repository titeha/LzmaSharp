using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterFilesTests
{
  [Fact]
  public void BuildArchive_БезФайловСоздаётПустойАрхив()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        Array.Empty<SevenZipArchiveWriterFile>(),
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void BuildArchive_ОдинПустойФайлСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        new[]
        {
                new SevenZipArchiveWriterFile("empty.txt", Array.Empty<byte>()),
        },
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

  [Fact]
  public void BuildArchive_ОдинНепустойФайлСоздаётCopyАрхивКоторыйЧитаетсяDecoderPath()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        new[]
        {
            new SevenZipArchiveWriterFile("file.bin", content),
        },
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal("file.bin", fileName);
    Assert.Equal(content, fileBytes);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_НесколькоФайловПокаВозвращаетNotSupported()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        new[]
        {
                new SevenZipArchiveWriterFile("a.txt", Array.Empty<byte>()),
                new SevenZipArchiveWriterFile("b.txt", Array.Empty<byte>()),
        },
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.NotSupported, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_NullСписокВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        null!,
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_NullContentВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        new[]
        {
                new SevenZipArchiveWriterFile("file.txt", null!),
        },
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }
}
