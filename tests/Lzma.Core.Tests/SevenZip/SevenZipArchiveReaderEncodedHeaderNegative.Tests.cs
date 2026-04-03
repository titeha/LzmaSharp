using System.Buffers.Binary;
using System.Collections.Generic;
using Lzma.Core.Checksums;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderEncodedHeaderNegativeTests
{
 [Fact]
 public void Read_EncodedHeader_WithoutUnpackInfo_ReturnsInvalidData()
 {
  byte[] nextHeader = BuildEncodedHeaderWithPackInfoOnly(packPos: 0, packSize: 1);
  byte[] packedStreams = [0xAA];
  byte[] archive = BuildArchive(packedStreams, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_WithZeroPackStreams_ReturnsInvalidData()
 {
  byte[] nextHeader = BuildEncodedHeaderWithCopy(packPos: 0, packSizes: [], unpackSizes: [1]);
  byte[] packedStreams = [];
  byte[] archive = BuildArchive(packedStreams, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_WithTwoPackStreams_ReturnsNotSupported()
 {
  byte[] nextHeader = BuildEncodedHeaderWithCopy(packPos: 0, packSizes: [1UL, 1UL], unpackSizes: [1]);
  byte[] packedStreams = [0xAA, 0xBB];
  byte[] archive = BuildArchive(packedStreams, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_PackPosBeyondPackedStreams_ReturnsInvalidData()
 {
  byte[] nextHeader = BuildEncodedHeaderWithCopy(packPos: 1, packSizes: [1], unpackSizes: [1]);
  byte[] packedStreams = [];
  byte[] archive = BuildArchive(packedStreams, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_PackSizeBeyondPackedStreamsTail_ReturnsInvalidData()
 {
  byte[] nextHeader = BuildEncodedHeaderWithCopy(packPos: 0, packSizes: [2], unpackSizes: [2]);
  byte[] packedStreams = [0xAA];
  byte[] archive = BuildArchive(packedStreams, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_SingleFolderWithTwoUnpackStreams_ReturnsNotSupported()
 {
  byte[] nextHeader = BuildEncodedHeaderWithTwoCopyCoders(
   packPos: 0,
   packSize: (ulong)ValidEmptyHeader.Length,
   unpackSize0: (ulong)ValidEmptyHeader.Length,
   unpackSize1: (ulong)ValidEmptyHeader.Length);
  byte[] archive = BuildArchive(ValidEmptyHeader, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_DecodedHeaderTruncated_ReturnsInvalidData()
 {
  byte[] decodedHeader = [SevenZipNid.Header];
  byte[] nextHeader = BuildEncodedHeaderWithCopy(
   packPos: 0,
   packSizes: [(ulong)decodedHeader.Length],
   unpackSizes: [(ulong)decodedHeader.Length]);
  byte[] archive = BuildArchive(decodedHeader, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 [Fact]
 public void Read_EncodedHeader_DecodedHeaderHasTrailingGarbage_ReturnsInvalidData()
 {
  byte[] decodedHeader = [.. ValidEmptyHeader, 0xFF];
  byte[] nextHeader = BuildEncodedHeaderWithCopy(
   packPos: 0,
   packSizes: [(ulong)decodedHeader.Length],
   unpackSizes: [(ulong)decodedHeader.Length]);
  byte[] archive = BuildArchive(decodedHeader, nextHeader);

  var reader = new SevenZipArchiveReader();

  SevenZipArchiveReadResult result = reader.Read(archive, out int bytesConsumed);

  Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
  Assert.Equal(archive.Length, bytesConsumed);
  Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);
  Assert.False(reader.Header.HasValue);
  Assert.True(reader.DecodedHeaderBytes.IsEmpty);
 }

 private static readonly byte[] ValidEmptyHeader =
 [
  SevenZipNid.Header,
  SevenZipNid.MainStreamsInfo,
  SevenZipNid.End,
  SevenZipNid.FilesInfo,
  0x00,
  SevenZipNid.End,
  SevenZipNid.End,
 ];

 private static byte[] BuildArchive(ReadOnlySpan<byte> packedStreams, ReadOnlySpan<byte> nextHeaderBytes)
 {
  byte[] archive = new byte[SevenZipSignatureHeader.Size + packedStreams.Length + nextHeaderBytes.Length];
  WriteSignatureHeader(
   archive,
   nextHeaderOffset: (ulong)packedStreams.Length,
   nextHeaderSize: (ulong)nextHeaderBytes.Length,
   nextHeaderCrc: Crc32.Compute(nextHeaderBytes));

  packedStreams.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size));
  nextHeaderBytes.CopyTo(archive.AsSpan(SevenZipSignatureHeader.Size + packedStreams.Length));
  return archive;
 }

 private static byte[] BuildEncodedHeaderWithPackInfoOnly(ulong packPos, ulong packSize)
 {
  List<byte> h =
  [
   SevenZipNid.EncodedHeader,
   SevenZipNid.PackInfo,
  ];

  WriteU64(h, packPos);
  WriteU64(h, 1);
  h.Add(SevenZipNid.Size);
  WriteU64(h, packSize);
  h.Add(SevenZipNid.End);
  h.Add(SevenZipNid.End);

  return [.. h];
 }

 private static byte[] BuildEncodedHeaderWithCopy(ulong packPos, ulong[] packSizes, ulong[] unpackSizes)
 {
  List<byte> h =
  [
   SevenZipNid.EncodedHeader,
   SevenZipNid.PackInfo,
  ];

  WriteU64(h, packPos);
  WriteU64(h, (ulong)packSizes.Length);
  h.Add(SevenZipNid.Size);
  for (int i = 0; i < packSizes.Length; i++)
   WriteU64(h, packSizes[i]);
  h.Add(SevenZipNid.End);

  h.Add(SevenZipNid.UnpackInfo);
  h.Add(SevenZipNid.Folder);
  WriteU64(h, 1);
  h.Add(0);

  WriteU64(h, 1);
  h.Add(0b0000_0001);
  h.Add(0x00);

  h.Add(SevenZipNid.CodersUnpackSize);
  for (int i = 0; i < unpackSizes.Length; i++)
   WriteU64(h, unpackSizes[i]);

  h.Add(SevenZipNid.End);
  h.Add(SevenZipNid.End);

  return [.. h];
 }

 private static byte[] BuildEncodedHeaderWithTwoCopyCoders(
  ulong packPos,
  ulong packSize,
  ulong unpackSize0,
  ulong unpackSize1)
 {
  List<byte> h =
  [
   SevenZipNid.EncodedHeader,
   SevenZipNid.PackInfo,
  ];

  WriteU64(h, packPos);
  WriteU64(h, 1);
  h.Add(SevenZipNid.Size);
  WriteU64(h, packSize);
  h.Add(SevenZipNid.End);

  h.Add(SevenZipNid.UnpackInfo);
  h.Add(SevenZipNid.Folder);
  WriteU64(h, 1);
  h.Add(0);

  WriteU64(h, 2);

  h.Add(0b0000_0001);
  h.Add(0x00);

  h.Add(0b0000_0001);
  h.Add(0x00);

  WriteU64(h, 1);
  WriteU64(h, 0);

  h.Add(SevenZipNid.CodersUnpackSize);
  WriteU64(h, unpackSize0);
  WriteU64(h, unpackSize1);

  h.Add(SevenZipNid.End);
  h.Add(SevenZipNid.End);

  return [.. h];
 }

 private static void WriteU64(List<byte> dst, ulong value)
 {
  Span<byte> tmp = stackalloc byte[10];
  SevenZipEncodedUInt64.WriteResult result = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);

  Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, result);

  for (int i = 0; i < written; i++)
   dst.Add(tmp[i]);
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
