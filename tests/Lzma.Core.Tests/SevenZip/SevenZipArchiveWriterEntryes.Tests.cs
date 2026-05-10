using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterFilesTests
{
  [Fact]
  public void BuildArchive_БезФайловСоздаётПустойАрхив()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        Array.Empty<SevenZipArchiveWriterEntry>(),
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
        [new SevenZipArchiveWriterEntry("empty.txt", [])],
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
        [new SevenZipArchiveWriterEntry("file.bin", content)],
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
  public void BuildArchive_НесколькоПустыхФайловСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("b.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("a.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("b.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        }]);
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
        [new SevenZipArchiveWriterEntry("file.txt", null!),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ПовреждениеНепустогоCopyФайлаДаётInvalidData()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    archive[SevenZipSignatureHeader.Size] ^= 0xFF;

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void BuildArchive_ПовреждениеФайловогоCrcВHeaderДаётInvalidData()
  {
    byte[] content = [1, 2, 3, 4, 5];

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", content),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    CorruptLastCrcPropertyInNextHeaderAndRefreshHeaderCrc(
        archive,
        packedDataLength: content.Length);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, decodeResult);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Theory]
  [InlineData("")]
  [InlineData("dir/file.txt")]
  [InlineData("dir\\file.txt")]
  [InlineData("bad\0name.txt")]
  public void BuildArchive_НекорректноеИмяНепустогоФайлаВозвращаетInvalidData(string fileName)
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(fileName, [1, 2, 3]),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_NullИмяНепустогоФайлаВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry(null!, [1, 2, 3]),],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_НесколькоФайловСНепустымФайломПокаВозвращаетNotSupported()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("b.txt", [1, 2, 3]),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.NotSupported, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_НесколькоПустыхФайловСНекорректнымИменемВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("dir/b.txt", []),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ОднаПустаяДиректорияСоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);

    Assert.Equal("dir", entry.Name);
    Assert.Empty(entry.Bytes);
    Assert.True(entry.IsDirectory);
  }

  [Fact]
  public void BuildArchive_ПустойФайлИДиректорияСоздаютАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", []),
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Collection(
        entries,
        [entry =>
        {
          Assert.Equal("a.txt", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.False(entry.IsDirectory);
        },
        entry =>
        {
          Assert.Equal("dir", entry.Name);
          Assert.Empty(entry.Bytes);
          Assert.True(entry.IsDirectory);
        }]);
  }

  [Fact]
  public void BuildArchive_ДиректорияСДаннымиВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir", [1, 2, 3], IsDirectory: true)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  private static void CorruptLastCrcPropertyInNextHeaderAndRefreshHeaderCrc(
    byte[] archive,
    int packedDataLength)
  {
    int nextHeaderStart = SevenZipSignatureHeader.Size + packedDataLength;

    Span<byte> nextHeader = archive.AsSpan(nextHeaderStart);

    int crcPropertyOffset = FindLastCrcPropertyOffset(nextHeader);

    Assert.True(crcPropertyOffset >= 0);

    nextHeader[crcPropertyOffset + 3] ^= 0xFF;

    uint nextHeaderCrc = Crc32.Compute(nextHeader);

    var signatureHeader = new SevenZipSignatureHeader(
        NextHeaderOffset: (ulong)packedDataLength,
        NextHeaderSize: (ulong)nextHeader.Length,
        NextHeaderCrc: nextHeaderCrc);

    signatureHeader.Write(archive);
  }

  private static int FindLastCrcPropertyOffset(ReadOnlySpan<byte> nextHeader)
  {
    int result = -1;

    for (int i = 0; i <= nextHeader.Length - 7; i++)
    {
      if (nextHeader[i] == SevenZipNid.Crc
          && nextHeader[i + 1] == 0x05
          && nextHeader[i + 2] == 0x01)
      {
        result = i;
      }
    }

    return result;
  }
}
