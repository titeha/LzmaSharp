using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterWinAttributesTests
{
  private const uint WindowsFileAttributeDirectory = 0x00000010;
  private const uint WindowsFileAttributeArchive = 0x00000020;

  [Fact]
  public void BuildArchive_EmptyEntriesПишетWinAttributesДляФайловИДиректорий()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
                new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
            ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true, true], filesInfo.WinAttribDefined!);
    Assert.Equal(
        [WindowsFileAttributeArchive, WindowsFileAttributeDirectory],
        filesInfo.WinAttrib!);
  }

  [Fact]
  public void BuildArchive_CopyEntriesПишетArchiveAttributeДляНепустыхФайлов()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.bin", [1, 2, 3]),
                new SevenZipArchiveWriterEntry("b.bin", [4, 5]),
            ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true, true], filesInfo.WinAttribDefined!);
    Assert.Equal(
        [WindowsFileAttributeArchive, WindowsFileAttributeArchive],
        filesInfo.WinAttrib!);
  }

  [Fact]
  public void BuildArchive_MixedEntriesПишетWinAttributesДляВсехEntry()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
                new SevenZipArchiveWriterEntry("dir/empty.txt", []),
                new SevenZipArchiveWriterEntry("dir/file.bin", [1, 2, 3]),
            ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true, true, true], filesInfo.WinAttribDefined!);
    Assert.Equal(
        [
            WindowsFileAttributeDirectory,
                WindowsFileAttributeArchive,
                WindowsFileAttributeArchive,
            ],
        filesInfo.WinAttrib!);
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
