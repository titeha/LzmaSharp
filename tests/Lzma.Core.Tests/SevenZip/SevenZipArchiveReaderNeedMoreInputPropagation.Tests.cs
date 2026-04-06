using System.Buffers.Binary;
using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderNeedMoreInputPropagationTests
{
  [Fact]
  public void Read_EmptyNextHeader_ReturnsInvalidData_AndBecomesTerminal()
  {
    byte[] archive = BuildArchive([]);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult res1 = reader.Read(archive, out int consumed1);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res1);
    Assert.Equal(archive.Length, consumed1);
    Assert.Null(reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);

    SevenZipArchiveReadResult res2 = reader.Read([], out int consumed2);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void Read_TruncatedHeaderInsideCompleteNextHeader_ReturnsInvalidData_AndBecomesTerminal()
  {
    byte[] archive = BuildArchive([SevenZipNid.Header]);

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult res1 = reader.Read(archive, out int consumed1);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res1);
    Assert.Equal(archive.Length, consumed1);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.False(reader.Header.HasValue);

    SevenZipArchiveReadResult res2 = reader.Read([], out int consumed2);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, res2);
    Assert.Equal(0, consumed2);
  }

  [Fact]
  public void DecodeToArray_EmptyNextHeader_ReturnsInvalidData_NotNeedMoreData()
  {
    byte[] archive = BuildArchive([]);

    SevenZipArchiveDecodeResult res = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, res);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  [Fact]
  public void DecodeToArray_TruncatedHeaderInsideCompleteNextHeader_ReturnsInvalidData_NotNeedMoreData()
  {
    byte[] archive = BuildArchive([SevenZipNid.Header]);

    SevenZipArchiveDecodeResult res = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, res);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(files);
  }

  private static byte[] BuildArchive(ReadOnlySpan<byte> nextHeaderBytes)
  {
    byte[] archive = new byte[SevenZipSignatureHeader.Size + nextHeaderBytes.Length];

    WriteSignatureHeader(
      archive,
      nextHeaderOffset: 0,
      nextHeaderSize: (ulong)nextHeaderBytes.Length,
      nextHeaderCrc: Crc32.Compute(nextHeaderBytes));

    nextHeaderBytes.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
    return archive;
  }

  private static void WriteSignatureHeader(
    Span<byte> file,
    ulong nextHeaderOffset,
    ulong nextHeaderSize,
    uint nextHeaderCrc)
  {
    SevenZipSignatureHeader.Signature.CopyTo(file);
    file[6] = SevenZipSignatureHeader.MajorVersion;
    file[7] = SevenZipSignatureHeader.MinorVersion;

    Span<byte> startHeader = stackalloc byte[20];
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(0, 8), nextHeaderOffset);
    BinaryPrimitives.WriteUInt64LittleEndian(startHeader.Slice(8, 8), nextHeaderSize);
    BinaryPrimitives.WriteUInt32LittleEndian(startHeader.Slice(16, 4), nextHeaderCrc);

    uint startHeaderCrc = Crc32.Compute(startHeader);
    BinaryPrimitives.WriteUInt32LittleEndian(file.Slice(8, 4), startHeaderCrc);
    startHeader.CopyTo(file.Slice(12, 20));
  }
}
