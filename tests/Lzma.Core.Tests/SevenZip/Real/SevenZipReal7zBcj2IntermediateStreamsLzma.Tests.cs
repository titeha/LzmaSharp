using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2IntermediateStreamsLzmaTests
{
  [Fact]
  public void TryDecodeBcj2InputStreams_РеальныйBcj2LzmaАрхив_ДекодируетЧетыреПромежуточныхПотокаСПравильнымиРазмерами()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/bcj2_x86_lzma_d1m_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int consumed));
    Assert.Equal(archive.Length, consumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.UnpackInfo);
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;
    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    ulong[]? folderUnpackSizes = unpackInfo.FolderUnpackSizes[0];

    Assert.NotNull(folderUnpackSizes);
    Assert.Equal(4, folder.PackedStreamIndices.Length);
    Assert.Equal(3, folder.BindPairs.Length);

    SevenZipFolderDecodeResult rr = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo,
        reader.PackedStreams.Span,
        folderIndex: 0,
        out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, rr);
    Assert.Equal(4, ranges.Length);

    int bcj2Index = -1;
    int inCursor = 0;

    for (int i = 0; i < folder.Coders.Length; i++)
    {
      SevenZipCoderInfo coder = folder.Coders[i];
      if (IsBcj2(coder.MethodId))
      {
        bcj2Index = i;
        break;
      }

      Assert.True(coder.NumInStreams <= int.MaxValue);
      inCursor += (int)coder.NumInStreams;
    }

    Assert.True(bcj2Index >= 0);

    SevenZipCoderInfo bcj2 = folder.Coders[bcj2Index];
    Assert.Equal(4UL, bcj2.NumInStreams);

    int bcj2InStart = inCursor;
    int[] expectedSizes = new int[4];

    for (int slot = 0; slot < 4; slot++)
    {
      ulong consumerIn = (ulong)(bcj2InStart + slot);
      bool found = false;
      ulong producerOut = 0;

      for (int i = 0; i < folder.BindPairs.Length; i++)
      {
        SevenZipBindPair bp = folder.BindPairs[i];
        if (bp.InIndex == consumerIn)
        {
          producerOut = bp.OutIndex;
          found = true;
          break;
        }
      }

      if (!found)
      {
        // Один из входов BCJ2 может идти напрямую из packed stream.
        int packOrdinal = -1;
        for (int i = 0; i < folder.PackedStreamIndices.Length; i++)
        {
          if (folder.PackedStreamIndices[i] == consumerIn)
          {
            packOrdinal = i;
            break;
          }
        }

        Assert.True(packOrdinal >= 0);
        expectedSizes[slot] = ranges[packOrdinal].Length;
        continue;
      }

      Assert.True(producerOut <= int.MaxValue);
      int outIndex = (int)producerOut;
      Assert.InRange(outIndex, 0, folderUnpackSizes!.Length - 1);

      ulong sizeU64 = folderUnpackSizes[outIndex];
      Assert.True(sizeU64 <= int.MaxValue);
      expectedSizes[slot] = (int)sizeU64;
    }

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo,
        reader.PackedStreams.Span,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, result);
    Assert.Equal(4, decoded.Length);

    for (int slot = 0; slot < 4; slot++)
      Assert.Equal(expectedSizes[slot], decoded[slot].Length);
  }

  [Fact]
  public void TryDecodeBcj2InputStreams_РеальныйBcj2LzmaАрхив_ПриНекорректнойДлинеPropertiesУProducerLzma_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/bcj2_x86_lzma_d1m_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int consumed));
    Assert.Equal(archive.Length, consumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo originalStreamsInfo = header.StreamsInfo;

    Assert.NotNull(originalStreamsInfo.UnpackInfo);
    SevenZipUnpackInfo originalUnpackInfo = originalStreamsInfo.UnpackInfo!;
    Assert.Single(originalUnpackInfo.Folders);

    SevenZipFolder originalFolder = originalUnpackInfo.Folders[0];
    SevenZipCoderInfo[] mutatedCoders = new SevenZipCoderInfo[originalFolder.Coders.Length];

    int lzmaCoderCount = 0;

    for (int i = 0; i < originalFolder.Coders.Length; i++)
    {
      SevenZipCoderInfo coder = originalFolder.Coders[i];
      if (!IsLzma(coder.MethodId))
      {
        mutatedCoders[i] = coder;
        continue;
      }

      lzmaCoderCount++;

      mutatedCoders[i] = new SevenZipCoderInfo(
          methodId: coder.MethodId,
          properties: [0x5D, 0x00, 0x00, 0x10], // 4 байта вместо обязательных 5
          numInStreams: coder.NumInStreams,
          numOutStreams: coder.NumOutStreams);
    }

    Assert.Equal(3, lzmaCoderCount);

    var mutatedFolder = new SevenZipFolder(
        Coders: mutatedCoders,
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

    SevenZipFolderDecodeResult result = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
        streamsInfo: mutatedStreamsInfo,
        packedStreams: reader.PackedStreams.Span,
        folderIndex: 0,
        decodedInputStreams: out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.InvalidData, result);
    Assert.Empty(decoded);
  }

  private static bool IsBcj2(byte[] methodId)
  {
    return methodId.Length == 1 && methodId[0] == 0x1B
        || methodId.Length == 4
        && methodId[0] == 0x03
        && methodId[1] == 0x03
        && methodId[2] == 0x01
        && methodId[3] == 0x1B;
  }

  private static bool IsLzma(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x03
        && methodId[1] == 0x01
        && methodId[2] == 0x01;
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
