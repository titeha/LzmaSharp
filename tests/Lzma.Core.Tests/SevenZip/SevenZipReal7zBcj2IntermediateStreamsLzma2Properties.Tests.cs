using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2IntermediateStreamsLzma2PropertiesTests
{
  [Fact]
  public void TryDecodeBcj2InputStreams_РеальныйBcj2Lzma2Архив_ПриПустыхPropertiesУProducerLzma2_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateMutatedLzma2Scenario(
        mutatedProperties: [],
        packedStreams: out byte[] packedStreams);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreams_РеальныйBcj2Lzma2Архив_ПриНедопустимомPropertiesByteУProducerLzma2_ВозвращаетInvalidData()
  {
    SevenZipStreamsInfo streamsInfo = CreateMutatedLzma2Scenario(
        mutatedProperties: [41],
        packedStreams: out byte[] packedStreams);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  [Fact]
  public void TryDecodeBcj2InputStreams_РеальныйBcj2Lzma2Архив_ПриСлишкомБольшомСловареУProducerLzma2_ВозвращаетNotSupported()
  {
    SevenZipStreamsInfo streamsInfo = CreateMutatedLzma2Scenario(
        mutatedProperties: [40],
        packedStreams: out byte[] packedStreams);

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: streamsInfo,
        packedStreams: packedStreams,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.NotSupported, result);
    Assert.Empty(decoded);
  }

  private static SevenZipStreamsInfo CreateMutatedLzma2Scenario(
      byte[] mutatedProperties,
      out byte[] packedStreams)
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_x86_lzma2_d1m_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int consumed));
    Assert.Equal(archive.Length, consumed);
    Assert.True(reader.Header.HasValue);

    packedStreams = reader.PackedStreams.Span.ToArray();

    SevenZipStreamsInfo originalStreamsInfo = reader.Header.Value.StreamsInfo;
    Assert.NotNull(originalStreamsInfo.UnpackInfo);

    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;
    Assert.Single(originalUnpackInfo.Folders);

    SevenZipFolder originalFolder = originalUnpackInfo.Folders[0];
    SevenZipCoderInfo[] mutatedCoders = new SevenZipCoderInfo[originalFolder.Coders.Length];

    int lzma2CoderCount = 0;

    for (int i = 0; i < originalFolder.Coders.Length; i++)
    {
      SevenZipCoderInfo coder = originalFolder.Coders[i];
      if (!IsLzma2(coder.MethodId))
      {
        mutatedCoders[i] = coder;
        continue;
      }

      lzma2CoderCount++;

      mutatedCoders[i] = new SevenZipCoderInfo(
          methodId: coder.MethodId,
          properties: mutatedProperties,
          numInStreams: coder.NumInStreams,
          numOutStreams: coder.NumOutStreams);
    }

    Assert.True(lzma2CoderCount > 0);

    var mutatedFolder = new SevenZipFolder(
        Coders: mutatedCoders,
        BindPairs: originalFolder.BindPairs,
        PackedStreamIndices: originalFolder.PackedStreamIndices,
        NumInStreams: originalFolder.NumInStreams,
        NumOutStreams: originalFolder.NumOutStreams);

    var mutatedUnpackInfo = new SevenZipUnpackInfo(
        folders: [mutatedFolder],
        folderUnpackSizes: originalUnpackInfo.FolderUnpackSizes);

    return new SevenZipStreamsInfo(
        packInfo: originalStreamsInfo.PackInfo,
        unpackInfo: mutatedUnpackInfo,
        subStreamsInfo: originalStreamsInfo.SubStreamsInfo);
  }

  private static bool IsLzma2(byte[] methodId)
  {
    return methodId.Length == 1
        && methodId[0] == SevenZipLzma2Coder.MethodIdByte;
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
