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

  [Fact]
  public void BuildArchive_ЧастичноЗаданныйMTimeБольшеВосьмиEntryПишетОжидаемыйPayload()
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

    DateTime thirdTimeUtc = new(
        2026,
        6,
        4,
        14,
        0,
        0,
        DateTimeKind.Utc);

    DateTime fourthTimeUtc = new(
        2026,
        6,
        4,
        15,
        0,
        0,
        DateTimeKind.Utc);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty1.txt", []),
            new SevenZipArchiveWriterEntry("a.bin", [1], LastWriteTimeUtc: firstTimeUtc),
            new SevenZipArchiveWriterEntry("dir1", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty2.txt", []),
            new SevenZipArchiveWriterEntry("b.bin", [2, 3], LastWriteTimeUtc: secondTimeUtc),
            new SevenZipArchiveWriterEntry("dir2", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty3.txt", []),
            new SevenZipArchiveWriterEntry("empty4.txt", []),
            new SevenZipArchiveWriterEntry("c.bin", [4, 5, 6], LastWriteTimeUtc: thirdTimeUtc),
            new SevenZipArchiveWriterEntry("dir3", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty5.txt", []),
            new SevenZipArchiveWriterEntry("d.bin", [7, 8, 9], LastWriteTimeUtc: fourthTimeUtc),
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

    Assert.True(filesInfo.HasMTime);
    Assert.NotNull(filesInfo.MTimeDefined);
    Assert.NotNull(filesInfo.MTime);

    Assert.Equal(
        [
            false,
            true,
            false,
            false,
            true,
            false,
            false,
            false,
            true,
            false,
            false,
            true,
        ],
        filesInfo.MTimeDefined!);

    Assert.Equal(
        [
            0UL,
            (ulong)firstTimeUtc.ToFileTimeUtc(),
            0UL,
            0UL,
            (ulong)secondTimeUtc.ToFileTimeUtc(),
            0UL,
            0UL,
            0UL,
            (ulong)thirdTimeUtc.ToFileTimeUtc(),
            0UL,
            0UL,
            (ulong)fourthTimeUtc.ToFileTimeUtc(),
        ],
        filesInfo.MTime!);

    AssertMTimePayload(
        reader.NextHeaderBytes.Span,
        expectedDefinedBytes: [0x48, 0x90],
        expectedTimes:
        [
            (ulong)firstTimeUtc.ToFileTimeUtc(),
            (ulong)secondTimeUtc.ToFileTimeUtc(),
            (ulong)thirdTimeUtc.ToFileTimeUtc(),
            (ulong)fourthTimeUtc.ToFileTimeUtc(),
        ]);
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

  private static void AssertMTimePayload(
    ReadOnlySpan<byte> nextHeader,
    byte[] expectedDefinedBytes,
    ulong[] expectedTimes)
  {
    int propertyOffset = FindMTimePropertyOffset(
        nextHeader,
        expectedDefinedBytes.Length,
        expectedTimes.Length);

    Assert.True(propertyOffset >= 0);

    int propertySizeOffset = propertyOffset + 1;
    int allAreDefinedOffset = propertySizeOffset + 1;
    int definedBitVectorOffset = allAreDefinedOffset + 1;
    int externalOffset = definedBitVectorOffset + expectedDefinedBytes.Length;
    int timesOffset = externalOffset + 1;

    Assert.Equal(
        (byte)(1 + expectedDefinedBytes.Length + 1 + (expectedTimes.Length * 8)),
        nextHeader[propertySizeOffset]);

    Assert.Equal(0x00, nextHeader[allAreDefinedOffset]);

    ReadOnlySpan<byte> actualDefinedBytes = nextHeader.Slice(
        definedBitVectorOffset,
        expectedDefinedBytes.Length);

    Assert.Equal(expectedDefinedBytes, actualDefinedBytes.ToArray());

    Assert.Equal(0x00, nextHeader[externalOffset]);

    for (int i = 0; i < expectedTimes.Length; i++)
    {
      ulong actualTime = ReadUInt64LittleEndian(
          nextHeader.Slice(timesOffset + (i * 8), 8));

      Assert.Equal(expectedTimes[i], actualTime);
    }
  }

  private static int FindMTimePropertyOffset(
      ReadOnlySpan<byte> nextHeader,
      int definedBitVectorLength,
      int timeCount)
  {
    byte expectedPropertySize = (byte)(1 + definedBitVectorLength + 1 + (timeCount * 8));

    for (int i = 0; i <= nextHeader.Length - 4 - definedBitVectorLength; i++)
    {
      if (nextHeader[i] == SevenZipNid.MTime
          && nextHeader[i + 1] == expectedPropertySize
          && nextHeader[i + 2] == 0x00)
      {
        return i;
      }
    }

    return -1;
  }

  private static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source)
  {
    return source[0]
        | ((ulong)source[1] << 8)
        | ((ulong)source[2] << 16)
        | ((ulong)source[3] << 24)
        | ((ulong)source[4] << 32)
        | ((ulong)source[5] << 40)
        | ((ulong)source[6] << 48)
        | ((ulong)source[7] << 56);
  }
}
