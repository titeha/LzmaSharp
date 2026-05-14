using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterMixedCopyStructureTests
{
  [Fact]
  public void BuildArchive_СмешанныеEntryЧерезCopyФормируютОжидаемуюСтруктуруHeader()
  {
    byte[] firstContent = [1, 2, 3];
    byte[] secondContent = [4, 5, 6, 7];

    uint firstCrc = Crc32.Compute(firstContent);
    uint secondCrc = Crc32.Compute(secondContent);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty.txt", []),
                new SevenZipArchiveWriterEntry("a.bin", firstContent),
                new SevenZipArchiveWriterEntry("dir", [], IsDirectory: true),
                new SevenZipArchiveWriterEntry("b.bin", secondContent),
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

    int packedDataLength = firstContent.Length + secondContent.Length;

    Assert.Equal((ulong)packedDataLength, signatureHeader.NextHeaderOffset);
    Assert.Equal((ulong)reader.NextHeaderBytes.Length, signatureHeader.NextHeaderSize);
    Assert.Equal(Crc32.Compute(reader.NextHeaderBytes.Span), signatureHeader.NextHeaderCrc);

    Assert.True(reader.NextHeaderKind.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind.Value);

    Assert.Equal(
        [1, 2, 3, 4, 5, 6, 7],
        reader.PackedStreams.ToArray());

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;

    AssertPackInfo(header, firstContent.Length, secondContent.Length, firstCrc, secondCrc);
    AssertUnpackInfo(header, firstContent.Length, secondContent.Length, firstCrc, secondCrc);
    AssertFilesInfo(header, firstCrc, secondCrc);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyStream,
        expectedBytes: [0xA0]);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyFile,
        expectedBytes: [0x80]);
  }

  [Fact]
  public void BuildArchive_СмешанныеEntryБольшеВосьмиФормируютОжидаемыеBitVector()
  {
    byte[] firstContent = [1];
    byte[] secondContent = [2, 3];
    byte[] thirdContent = [4, 5, 6];

    uint firstCrc = Crc32.Compute(firstContent);
    uint secondCrc = Crc32.Compute(secondContent);
    uint thirdCrc = Crc32.Compute(thirdContent);

    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("empty1.txt", []),
            new SevenZipArchiveWriterEntry("a.bin", firstContent),
            new SevenZipArchiveWriterEntry("dir1", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty2.txt", []),
            new SevenZipArchiveWriterEntry("b.bin", secondContent),
            new SevenZipArchiveWriterEntry("dir2", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty3.txt", []),
            new SevenZipArchiveWriterEntry("empty4.txt", []),
            new SevenZipArchiveWriterEntry("c.bin", thirdContent),
            new SevenZipArchiveWriterEntry("dir3", [], IsDirectory: true),
            new SevenZipArchiveWriterEntry("empty5.txt", []),
            new SevenZipArchiveWriterEntry("empty6.txt", []),
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

    Assert.Equal(
        [1, 2, 3, 4, 5, 6],
        reader.PackedStreams.ToArray());

    SevenZipFilesInfo filesInfo = reader.Header.Value.FilesInfo;

    Assert.Equal(12UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);

    Assert.Equal(
        [
            true,
            false,
            true,
            true,
            false,
            true,
            true,
            true,
            false,
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
            false,
            false,
            true,
            false,
            false,
            true,
            true,
            false,
            false,
            true,
            true,
        ],
        filesInfo.EmptyFiles!);

    Assert.True(filesInfo.HasCrc);
    Assert.NotNull(filesInfo.CrcDefined);
    Assert.NotNull(filesInfo.Crc);

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
            false,
        ],
        filesInfo.CrcDefined!);

    Assert.Equal(
        [
            0U,
            firstCrc,
            0U,
            0U,
            secondCrc,
            0U,
            0U,
            0U,
            thirdCrc,
            0U,
            0U,
            0U,
        ],
        filesInfo.Crc!);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyStream,
        expectedBytes: [0xB7, 0x70]);

    AssertBitVectorProperty(
        reader.NextHeaderBytes.Span,
        SevenZipNid.EmptyFile,
        expectedBytes: [0xAD, 0x80]);

    AssertFilesInfoCrcDefinedBitVectorProperty(
        reader.NextHeaderBytes.Span,
        expectedBytes: [0x48, 0x80]);
  }

  private static void AssertFilesInfoCrcDefinedBitVectorProperty(
    ReadOnlySpan<byte> nextHeader,
    byte[] expectedBytes)
  {
    int propertyOffset = FindFilesInfoCrcPropertyOffset(nextHeader, expectedBytes.Length);

    Assert.True(propertyOffset >= 0);

    int propertySizeOffset = propertyOffset + 1;
    int allAreDefinedOffset = propertySizeOffset + 1;
    int bitVectorOffset = allAreDefinedOffset + 1;

    Assert.Equal((byte)(1 + expectedBytes.Length + (3 * 4)), nextHeader[propertySizeOffset]);
    Assert.Equal(0x00, nextHeader[allAreDefinedOffset]);

    ReadOnlySpan<byte> actualBytes = nextHeader.Slice(
        bitVectorOffset,
        expectedBytes.Length);

    Assert.Equal(expectedBytes, actualBytes.ToArray());
  }

  private static int FindFilesInfoCrcPropertyOffset(
      ReadOnlySpan<byte> nextHeader,
      int expectedBitVectorLength)
  {
    byte expectedPropertySize = (byte)(1 + expectedBitVectorLength + (3 * 4));

    for (int i = 0; i <= nextHeader.Length - 3 - expectedBitVectorLength; i++)
      if (nextHeader[i] == SevenZipNid.Crc
                && nextHeader[i + 1] == expectedPropertySize
                && nextHeader[i + 2] == 0x00)
        return i;

    return -1;
  }

  private static void AssertPackInfo(
      SevenZipHeader header,
      int firstLength,
      int secondLength,
      uint firstCrc,
      uint secondCrc)
  {
    Assert.True(header.StreamsInfo.PackInfo.HasValue);

    SevenZipPackInfo packInfo = header.StreamsInfo.PackInfo.Value;

    Assert.Equal(0UL, packInfo.PackPos);
    Assert.Equal([(ulong)firstLength, (ulong)secondLength], packInfo.PackSizes);

    Assert.True(packInfo.HasCrc);
    Assert.NotNull(packInfo.CrcDefined);
    Assert.NotNull(packInfo.Crc);

    Assert.Equal([true, true], packInfo.CrcDefined!);
    Assert.Equal([firstCrc, secondCrc], packInfo.Crc!);
  }

  private static void AssertUnpackInfo(
      SevenZipHeader header,
      int firstLength,
      int secondLength,
      uint firstCrc,
      uint secondCrc)
  {
    Assert.NotNull(header.StreamsInfo.UnpackInfo);

    SevenZipUnpackInfo unpackInfo = header.StreamsInfo.UnpackInfo!;

    Assert.Equal(2, unpackInfo.Folders.Length);

    AssertCopyFolder(unpackInfo.Folders[0]);
    AssertCopyFolder(unpackInfo.Folders[1]);

    Assert.Equal(2, unpackInfo.FolderUnpackSizes.Length);
    Assert.Equal([(ulong)firstLength], unpackInfo.FolderUnpackSizes[0]);
    Assert.Equal([(ulong)secondLength], unpackInfo.FolderUnpackSizes[1]);

    Assert.True(unpackInfo.HasFolderCrcDefined);
    Assert.True(unpackInfo.HasFolderCrc);
    Assert.NotNull(unpackInfo.FolderCrcDefined);
    Assert.NotNull(unpackInfo.FolderCrc);

    Assert.Equal([true, true], unpackInfo.FolderCrcDefined!);
    Assert.Equal([firstCrc, secondCrc], unpackInfo.FolderCrc!);
  }

  private static void AssertCopyFolder(SevenZipFolder folder)
  {
    Assert.Equal(1UL, folder.NumInStreams);
    Assert.Equal(1UL, folder.NumOutStreams);
    Assert.Empty(folder.BindPairs);

    ulong packedStreamIndex = Assert.Single(folder.PackedStreamIndices);
    Assert.Equal(0UL, packedStreamIndex);

    SevenZipCoderInfo coder = Assert.Single(folder.Coders);

    Assert.Equal([0x00], coder.MethodId);
    Assert.Empty(coder.Properties);
    Assert.Equal(1UL, coder.NumInStreams);
    Assert.Equal(1UL, coder.NumOutStreams);
  }

  private static void AssertFilesInfo(
      SevenZipHeader header,
      uint firstCrc,
      uint secondCrc)
  {
    SevenZipFilesInfo filesInfo = header.FilesInfo;

    Assert.Equal(4UL, filesInfo.FileCount);

    Assert.True(filesInfo.HasNames);
    Assert.NotNull(filesInfo.Names);
    Assert.Equal(["empty.txt", "a.bin", "dir", "b.bin"], filesInfo.Names!);

    Assert.True(filesInfo.HasEmptyStreams);
    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal([true, false, true, false], filesInfo.EmptyStreams!);

    Assert.True(filesInfo.HasEmptyFiles);
    Assert.NotNull(filesInfo.EmptyFiles);

    // EmptyFile=true только для empty file.
    // Для non-empty файлов значение false, потому что они не являются empty stream.
    // Для директории EmptyStream=true и EmptyFile=false.
    Assert.Equal([true, false, false, false], filesInfo.EmptyFiles!);

    Assert.False(filesInfo.HasAnti);
    Assert.Null(filesInfo.Anti);

    Assert.True(filesInfo.HasCrc);
    Assert.NotNull(filesInfo.CrcDefined);
    Assert.NotNull(filesInfo.Crc);

    Assert.Equal([false, true, false, true], filesInfo.CrcDefined!);
    Assert.Equal([0U, firstCrc, 0U, secondCrc], filesInfo.Crc!);
  }

  private static void AssertBitVectorProperty(
      ReadOnlySpan<byte> nextHeader,
      byte propertyId,
      byte[] expectedBytes)
  {
    int propertyOffset = FindPropertyOffset(nextHeader, propertyId);

    Assert.True(propertyOffset >= 0);

    Assert.Equal((byte)expectedBytes.Length, nextHeader[propertyOffset + 1]);

    ReadOnlySpan<byte> actualBytes = nextHeader.Slice(
        propertyOffset + 2,
        expectedBytes.Length);

    Assert.Equal(expectedBytes, actualBytes.ToArray());
  }

  private static int FindPropertyOffset(ReadOnlySpan<byte> nextHeader, byte propertyId)
  {
    for (int i = 0; i < nextHeader.Length; i++)
      if (nextHeader[i] == propertyId)
        return i;

    return -1;
  }
}
