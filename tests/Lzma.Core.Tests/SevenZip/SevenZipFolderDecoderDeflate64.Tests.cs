using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderDeflate64Tests
{
  [Fact]
  public void DecodeFolderToArray_Deflate64_РеальныйАрхив_ВозвращаетИсходныеБайты()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate64_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);

    byte[] expected = new byte[16 * 1024];
    expected.AsSpan().Fill(0x41);
    Assert.Equal(expected, output);
  }

  [Fact]
  public void DecodeFolderToArray_Deflate64_РеальныйАрхив_ПриМеньшемUnpackSize_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate64_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo originalStreamsInfo = header.StreamsInfo;
    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;

    Assert.Single(originalUnpackInfo.Folders);
    Assert.Single(originalUnpackInfo.FolderUnpackSizes);
    Assert.Single(originalUnpackInfo.FolderUnpackSizes[0]);

    SevenZipFolder folder = originalUnpackInfo.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    ulong originalUnpackSize = originalUnpackInfo.FolderUnpackSizes[0][0];
    Assert.True(originalUnpackSize > 0);

    var mutatedUnpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [[originalUnpackSize - 1]]);

    var mutatedStreamsInfo = new SevenZipStreamsInfo(
        packInfo: originalStreamsInfo.PackInfo,
        unpackInfo: mutatedUnpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: mutatedStreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Deflate64_РеальныйАрхив_ПриБольшемUnpackSize_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate64_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo originalStreamsInfo = header.StreamsInfo;
    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;

    Assert.Single(originalUnpackInfo.Folders);
    Assert.Single(originalUnpackInfo.FolderUnpackSizes);
    Assert.Single(originalUnpackInfo.FolderUnpackSizes[0]);

    SevenZipFolder folder = originalUnpackInfo.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    ulong originalUnpackSize = originalUnpackInfo.FolderUnpackSizes[0][0];
    Assert.True(originalUnpackSize < int.MaxValue);

    var mutatedUnpackInfo = new SevenZipUnpackInfo(
        folders: [folder],
        folderUnpackSizes: [[originalUnpackSize + 1]]);

    var mutatedStreamsInfo = new SevenZipStreamsInfo(
        packInfo: originalStreamsInfo.PackInfo,
        unpackInfo: mutatedUnpackInfo,
        subStreamsInfo: null);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: mutatedStreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  [Fact]
  public void DecodeFolderToArray_Deflate64_РеальныйАрхив_ПриОбрезанномPackedStream_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate64_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo originalStreamsInfo = header.StreamsInfo;

    Assert.NotNull(originalStreamsInfo.PackInfo);
    Assert.NotNull(originalStreamsInfo.UnpackInfo);

    SevenZipPackInfo originalPackInfo = originalStreamsInfo.PackInfo.Value;
    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;

    Assert.Single(originalPackInfo.PackSizes);
    Assert.Single(originalUnpackInfo.Folders);

    SevenZipFolder folder = originalUnpackInfo.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    ulong originalPackSize = originalPackInfo.PackSizes[0];
    Assert.True(originalPackSize > 4);

    var mutatedPackInfo = new SevenZipPackInfo(
        packPos: originalPackInfo.PackPos,
        packSizes: [originalPackSize - 4]);

    var mutatedStreamsInfo = new SevenZipStreamsInfo(
        packInfo: mutatedPackInfo,
        unpackInfo: originalStreamsInfo.UnpackInfo,
        subStreamsInfo: originalStreamsInfo.SubStreamsInfo);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: mutatedStreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static bool IsDeflate64(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x01
        && methodId[2] == 0x09;
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
