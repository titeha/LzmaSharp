using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zTwoFoldersDeflateAndBzip2Tests
{
  private static readonly byte[] _deflateMethodId = [0x04, 0x01, 0x08]; // { 04 01 08 }
  private static readonly byte[] _bzip2MethodId = [0x04, 0x02, 0x02];   // { 04 02 02 }

  [Fact]
  public void DecodeToArray_Real7z_TwoFolders_DeflateAndBzip2_HeaderNotEncoded_RangesAndDecode_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/two_folders_deflate_and_bzip2_mhc_off.7z");

    var reader = new SevenZipArchiveReader();

    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);

    SevenZipPackInfo packInfo = (streamsInfo.PackInfo ?? default)!;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo!;

    Assert.Equal(2, unpackInfo.Folders.Length);
    Assert.Equal(2, packInfo.PackSizes.Length);

    // Порядок folder'ов не фиксируем: нам важно, что ОБА метода реально присутствуют.
    Assert.Contains(
     unpackInfo.Folders,
     f => f.Coders.Length == 1 && f.Coders[0].MethodId.AsSpan().SequenceEqual(_deflateMethodId));

    Assert.Contains(
     unpackInfo.Folders,
     f => f.Coders.Length == 1 && f.Coders[0].MethodId.AsSpan().SequenceEqual(_bzip2MethodId));

    // Проверяем адресацию packed streams для обоих folder'ов.
    HashSet<uint> seenPackStreamIndices = [];

    for (int folderIndex = 0; folderIndex < unpackInfo.Folders.Length; folderIndex++)
    {
      Assert.Equal(
       SevenZipFolderDecodeResult.Ok,
       SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
        streamsInfo,
        reader.PackedStreams.Span,
        folderIndex: folderIndex,
        out SevenZipFolderPackedStreamRange[] ranges));

      Assert.Single(ranges);

      uint packStreamIndexU32 = ranges[0].PackStreamIndex;

      Assert.True(packStreamIndexU32 <= int.MaxValue);
      int packStreamIndex = (int)packStreamIndexU32;

      // Каждый folder должен сидеть на своём packed stream.
      Assert.True(
       seenPackStreamIndices.Add(packStreamIndexU32),
       "Ожидали, что каждый folder использует уникальный packed stream.");

      // expectedOffset = PackPos + sum(PackSizes[0..packStreamIndex))
      ulong expectedOffsetU64 = packInfo.PackPos;

      for (int i = 0; i < packStreamIndex; i++)
        expectedOffsetU64 += packInfo.PackSizes[i];

      Assert.True(expectedOffsetU64 <= int.MaxValue);

      Assert.True(packInfo.PackSizes[packStreamIndex] <= int.MaxValue);

      Assert.Equal((int)expectedOffsetU64, ranges[0].Offset);
      Assert.Equal((int)packInfo.PackSizes[packStreamIndex], ranges[0].Length);
    }

    Assert.True(seenPackStreamIndices.SetEquals([0u, 1u]));

    // Реальный decode.
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
     archive,
     out SevenZipDecodedFile[] files,
     out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal(2, files.Length);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);

    foreach (var f in files)
      byName.Add(f.Name.Replace('\\', '/'), f);

    Assert.Equal(MakeFilledBytes(24 * 1024, 0x44), byName["a_deflate.bin"].Bytes);
    Assert.Equal(MakeFilledBytes(32 * 1024, 0x42), byName["b_bzip2.bin"].Bytes);
  }

  private static byte[] MakeFilledBytes(int length, byte value)
  {
    var bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
