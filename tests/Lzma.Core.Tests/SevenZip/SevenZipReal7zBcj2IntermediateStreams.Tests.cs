using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2IntermediateStreamsTests
{
  [Fact]
  public void TryDecodeBcj2InputStreams_Real7z_Bcj2_DecodesFourStreams_WithExpectedSizes()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_x86_lzma2_d1m_mhc.7z");

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

    SevenZipFolderDecodeResult rr = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
      streamsInfo,
      reader.PackedStreams.Span,
      folderIndex: 0,
      out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, rr);
    Assert.Equal(4, ranges.Length);

    // Находим BCJ2 coder и его start InIndex.
    int bcj2Index = -1;
    int inCursor = 0;

    for (int i = 0; i < folder.Coders.Length; i++)
    {
      SevenZipCoderInfo c = folder.Coders[i];

      if (IsBcj2(c.MethodId))
      {
        bcj2Index = i;
        break;
      }

      inCursor += (int)c.NumInStreams;
    }

    Assert.True(bcj2Index >= 0);

    SevenZipCoderInfo bcj2 = folder.Coders[bcj2Index];
    Assert.Equal(4UL, bcj2.NumInStreams);

    int bcj2InStart = inCursor;

    // Для каждого входа BCJ2 берём producer OutIndex из BindPairs и ожидаемый size из FolderUnpackSizes[OutIndex].
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
        // Для BCJ2 один из входных потоков может быть unbound и идти из packed stream напрямую.
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

        // Ожидаемый размер = размер packed stream (т.к. это raw поток без распаковки).
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

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.TryDecodeBcj2InputStreamsToArrays(
      streamsInfo,
      reader.PackedStreams.Span,
      folderIndex: 0,
      out byte[][] decoded);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r);
    Assert.Equal(4, decoded.Length);

    for (int slot = 0; slot < 4; slot++)
      Assert.Equal(expectedSizes[slot], decoded[slot].Length);
  }

  private static bool IsBcj2(byte[] methodId)
  {
    return
      methodId.Length == 1 && methodId[0] == 0x1B ||
      methodId.Length == 4 &&
      methodId[0] == 0x03 &&
      methodId[1] == 0x03 &&
      methodId[2] == 0x01 &&
      methodId[3] == 0x1B;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
