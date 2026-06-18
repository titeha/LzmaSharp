using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterWinAttributesTests
{
  private const uint _windowsFileAttributeDirectory = 0x00000010;
  private const uint _windowsFileAttributeArchive = 0x00000020;
  private const uint _windowsFileAttributeReadOnly = 0x00000001;

  [Fact]
  public void BuildArchive_EmptyEntriesПишетWinAttributesДляФайловИДиректорий()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
    [
      new SevenZipArchiveWriterEntry("empty.txt", []),
      new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
    ], out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true, true], filesInfo.WinAttribDefined!);
    Assert.Equal(
        [_windowsFileAttributeArchive, _windowsFileAttributeDirectory],
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
        [_windowsFileAttributeArchive, _windowsFileAttributeArchive],
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
            _windowsFileAttributeDirectory,
                _windowsFileAttributeArchive,
                _windowsFileAttributeArchive,
            ],
        filesInfo.WinAttrib);
  }

  [Fact]
  public void BuildArchive_ДевятьEntryПишетОжидаемыйWinAttributesPayload()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("dir1", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty1.txt", []),
            new SevenZipArchiveWriterEntry("file1.bin", [1]),
            new SevenZipArchiveWriterEntry("dir2", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty2.txt", []),
            new SevenZipArchiveWriterEntry("file2.bin", [2, 3]),
            new SevenZipArchiveWriterEntry("dir3", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty3.txt", []),
            new SevenZipArchiveWriterEntry("file3.bin", [4, 5, 6]),
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

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal(
        [true, true, true, true, true, true, true, true, true],
        filesInfo.WinAttribDefined!);

    Assert.Equal(
        [
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
        ],
        filesInfo.WinAttrib!);

    AssertWinAttributesPayload(
        reader.NextHeaderBytes.Span,
        [
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
            _windowsFileAttributeDirectory,
            _windowsFileAttributeArchive,
            _windowsFileAttributeArchive,
        ]);
  }

  [Fact]
  public void BuildArchive_ФайлСПользовательскимиWinAttributesПишетИхВFilesInfo()
  {
    uint attributes = _windowsFileAttributeArchive | _windowsFileAttributeReadOnly;

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("file.bin", [1, 2, 3], WindowsAttributes: attributes)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true], filesInfo.WinAttribDefined!);
    Assert.Equal([attributes], filesInfo.WinAttrib!);
  }

  [Fact]
  public void BuildArchive_ДиректорияСПользовательскимиWinAttributesПишетИхВFilesInfo()
  {
    uint attributes = _windowsFileAttributeDirectory | _windowsFileAttributeReadOnly;

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true, WindowsAttributes: attributes)],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipFilesInfo filesInfo = ReadFilesInfo(archive);

    Assert.True(filesInfo.HasWinAttrib);
    Assert.NotNull(filesInfo.WinAttribDefined);
    Assert.NotNull(filesInfo.WinAttrib);

    Assert.Equal([true], filesInfo.WinAttribDefined!);
    Assert.Equal([attributes], filesInfo.WinAttrib!);
  }

  [Fact]
  public void BuildArchive_ДиректорияБезDirectoryAttributeВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry(
                "dir",
                [],
                IsDirectory: true,
                WindowsAttributes: _windowsFileAttributeArchive),
        ],
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.InvalidData, writeResult);
    Assert.Empty(archive);
  }

  [Fact]
  public void BuildArchive_ФайлСDirectoryAttributeВозвращаетInvalidData()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry(
                "file.bin",
                [1, 2, 3],
                WindowsAttributes: _windowsFileAttributeDirectory),
        ],
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

  private static void AssertWinAttributesPayload(
    ReadOnlySpan<byte> nextHeader,
    uint[] expectedAttributes)
  {
    int propertyOffset = FindWinAttributesPropertyOffset(
        nextHeader,
        expectedAttributes.Length);

    Assert.True(propertyOffset >= 0);

    int propertySizeOffset = propertyOffset + 1;
    int allAreDefinedOffset = propertySizeOffset + 1;
    int externalOffset = allAreDefinedOffset + 1;
    int attributesOffset = externalOffset + 1;

    Assert.Equal(
        (byte)(2 + (expectedAttributes.Length * 4)),
        nextHeader[propertySizeOffset]);

    Assert.Equal(0x01, nextHeader[allAreDefinedOffset]);
    Assert.Equal(0x00, nextHeader[externalOffset]);

    for (int i = 0; i < expectedAttributes.Length; i++)
    {
      uint actualAttribute = ReadUInt32LittleEndian(
          nextHeader.Slice(attributesOffset + (i * 4), 4));

      Assert.Equal(expectedAttributes[i], actualAttribute);
    }
  }

  private static int FindWinAttributesPropertyOffset(
      ReadOnlySpan<byte> nextHeader,
      int attributeCount)
  {
    byte expectedPropertySize = (byte)(2 + (attributeCount * 4));

    for (int i = 0; i <= nextHeader.Length - 4; i++)
      if (nextHeader[i] == SevenZipNid.WinAttrib
                && nextHeader[i + 1] == expectedPropertySize
                && nextHeader[i + 2] == 0x01
                && nextHeader[i + 3] == 0x00)
        return i;

    return -1;
  }

  private static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source)
  {
    return source[0]
        | ((uint)source[1] << 8)
        | ((uint)source[2] << 16)
        | ((uint)source[3] << 24);
  }
}
