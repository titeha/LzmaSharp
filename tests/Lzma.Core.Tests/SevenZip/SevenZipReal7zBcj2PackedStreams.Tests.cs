using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBcj2PackedStreamsTests
{
  [Fact]
  public void Read_Real7z_Bcj2_FourPackedStreams_RangesMatchPackInfo()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/bcj2_x86_lzma2_d1m_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int consumed));
    Assert.Equal(archive.Length, consumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);

    SevenZipPackInfo packInfo = (streamsInfo.PackInfo ?? default)!;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    Assert.Single(unpackInfo.Folders);
    SevenZipFolder folder = unpackInfo.Folders[0];

    // Для BCJ2 ожидаем 4 packed streams (4 сжатых потока под BCJ2).
    Assert.Equal(4, folder.PackedStreamIndices.Length);
    Assert.Equal(4, packInfo.PackSizes.Length);

    SevenZipFolderDecodeResult r = SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
      streamsInfo,
      reader.PackedStreams.Span,
      folderIndex: 0,
      out SevenZipFolderPackedStreamRange[] ranges);

    Assert.Equal(SevenZipFolderDecodeResult.Ok, r);
    Assert.Equal(4, ranges.Length);

    ulong expectedOffsetU64 = packInfo.PackPos;

    for (int i = 0; i < ranges.Length; i++)
    {
      Assert.Equal((uint)i, ranges[i].PackStreamIndex);
      Assert.Equal(folder.PackedStreamIndices[i], ranges[i].FolderInIndex);

      ulong expectedLenU64 = packInfo.PackSizes[i];

      Assert.True(expectedOffsetU64 <= int.MaxValue);
      Assert.True(expectedLenU64 <= int.MaxValue);

      int expectedOffset = (int)expectedOffsetU64;
      int expectedLen = (int)expectedLenU64;

      Assert.Equal(expectedOffset, ranges[i].Offset);
      Assert.Equal(expectedLen, ranges[i].Length);

      // диапазон обязан быть валиден
      ReadOnlySpan<byte> slice = reader.PackedStreams.Span.Slice(ranges[i].Offset, ranges[i].Length);
      Assert.Equal(expectedLen, slice.Length);

      expectedOffsetU64 += expectedLenU64;
    }

    // Должны идти подряд
    for (int i = 0; i + 1 < ranges.Length; i++)
      Assert.Equal(ranges[i].Offset + ranges[i].Length, ranges[i + 1].Offset);
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
