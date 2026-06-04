using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterMTimeTests
{
  [Fact]
  public void BuildArchive_EntryWithoutLastWriteTimeНеПишетMTime()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", [])],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.False(filesInfo.HasMTime);
    Assert.Null(filesInfo.MTimeDefined);
    Assert.Null(filesInfo.MTime);
  }

  [Fact]
  public void BuildArchive_EntryWithLastWriteTimeUtcПишетMTime()
  {
    DateTime lastWriteTimeUtc = new(
        2026,
        6,
        4,
        12,
        30,
        15,
        DateTimeKind.Utc);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", [], LastWriteTimeUtc: lastWriteTimeUtc)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasMTime);
    Assert.NotNull(filesInfo.MTimeDefined);
    Assert.NotNull(filesInfo.MTime);

    Assert.Equal([true], filesInfo.MTimeDefined!);
    Assert.Equal([(ulong)lastWriteTimeUtc.ToFileTimeUtc()], filesInfo.MTime!);
  }

  [Fact]
  public void BuildArchive_ЧастичноЗаданныйLastWriteTimeUtcПишетDefinedBitVector()
  {
    DateTime firstTimeUtc = new(
        2026,
        6,
        4,
        12,
        0,
        0,
        DateTimeKind.Utc);

    DateTime secondTimeUtc = new(
        2026,
        6,
        4,
        13,
        0,
        0,
        DateTimeKind.Utc);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
                new SevenZipArchiveWriterEntry("a.bin", [1, 2, 3], LastWriteTimeUtc: firstTimeUtc),
                new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
                new SevenZipArchiveWriterEntry("b.bin", [4, 5], LastWriteTimeUtc: secondTimeUtc),
            ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasMTime);
    Assert.NotNull(filesInfo.MTimeDefined);
    Assert.NotNull(filesInfo.MTime);

    Assert.Equal([false, true, false, true], filesInfo.MTimeDefined!);
    Assert.Equal(
        [
            0UL,
                (ulong)firstTimeUtc.ToFileTimeUtc(),
                0UL,
                (ulong)secondTimeUtc.ToFileTimeUtc(),
            ],
        filesInfo.MTime!);
  }

  [Fact]
  public void BuildArchive_LastWriteTimeUnspecifiedВозвращаетInvalidData()
  {
    DateTime lastWriteTime = new(
        2026,
        6,
        4,
        12,
        0,
        0,
        DateTimeKind.Unspecified);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", [], LastWriteTimeUtc: lastWriteTime)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_LastWriteTimeLocalВозвращаетInvalidData()
  {
    DateTime lastWriteTime = new(
        2026,
        6,
        4,
        12,
        0,
        0,
        DateTimeKind.Local);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.txt", [], LastWriteTimeUtc: lastWriteTime)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  private static SevenZipFilesInfo ReadFilesInfo(byte[] archive)
  {
    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult readResult = reader.Read(
        archive,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, readResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.True(reader.Header.HasValue);

    return reader.Header.Value.FilesInfo;
  }
}
