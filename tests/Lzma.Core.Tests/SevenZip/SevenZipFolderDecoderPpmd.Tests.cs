using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFolderDecoderPpmdTests
{
  [Fact]
  public void DecodeFolderToArray_Ppmd_РеальныйАрхив_ВозвращаетИсходныеБайты()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/ppmd_singlefile_mhc.7z");

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
    Assert.True(IsPpmdMethodId(folder.Coders[0].MethodId));
    Assert.NotNull(folder.Coders[0].Properties);
    Assert.Equal(5, folder.Coders[0].Properties!.Length);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: streamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(CreatePpmdTextBytes(), output);
  }

  [Fact]
  public void DecodeFolderToArray_Ppmd_РеальныйАрхив_ПриНекорректнойДлинеProperties_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/ppmd_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo originalStreamsInfo = header.StreamsInfo;
    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;

    Assert.Single(originalUnpackInfo.Folders);

    SevenZipFolder originalFolder = originalUnpackInfo.Folders[0];
    Assert.Single(originalFolder.Coders);

    SevenZipCoderInfo originalCoder = originalFolder.Coders[0];
    Assert.True(IsPpmdMethodId(originalCoder.MethodId));
    Assert.NotNull(originalCoder.Properties);
    Assert.Equal(5, originalCoder.Properties!.Length);

    byte[] invalidProperties = originalCoder.Properties![..4];

    var mutatedCoder = new SevenZipCoderInfo(
        methodId: originalCoder.MethodId,
        properties: invalidProperties,
        numInStreams: originalCoder.NumInStreams,
        numOutStreams: originalCoder.NumOutStreams);

    var mutatedFolder = new SevenZipFolder(
        Coders: [mutatedCoder],
        BindPairs: originalFolder.BindPairs,
        PackedStreamIndices: originalFolder.PackedStreamIndices,
        NumInStreams: originalFolder.NumInStreams,
        NumOutStreams: originalFolder.NumOutStreams);

    var mutatedUnpackInfo = new SevenZipUnpackInfo(
        folders: [mutatedFolder],
        folderUnpackSizes: originalUnpackInfo.FolderUnpackSizes);

    var mutatedStreamsInfo = new SevenZipStreamsInfo(
        packInfo: originalStreamsInfo.PackInfo,
        unpackInfo: mutatedUnpackInfo,
        subStreamsInfo: originalStreamsInfo.SubStreamsInfo);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.DecodeFolderToArray(
        streamsInfo: mutatedStreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        output: out byte[] output);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(output);
  }

  private static bool IsPpmdMethodId(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x03
        && methodId[1] == 0x04
        && methodId[2] == 0x01;
  }

  private static byte[] CreatePpmdTextBytes()
  {
    const string line1 = "PPMd real test line 01: alpha beta gamma delta epsilon zeta.\n";
    const string line2 = "PPMd real test line 02: the quick brown fox jumps over the lazy dog.\n";
    const string line3 = "PPMd real test line 03: 0123456789 repeated text for compression.\n";

    var sb = new StringBuilder(capacity: 32 * 1024);
    for (int i = 0; i < 180; i++)
    {
      sb.Append(line1);
      sb.Append(line2);
      sb.Append(line3);
    }

    return Encoding.ASCII.GetBytes(sb.ToString());
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
