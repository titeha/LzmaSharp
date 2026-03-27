using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zTwoFoldersLzma2Ms1fTests
{
  [Fact]
  public void DecodeToArray_Real7z_TwoFolders_Lzma2_Ms1f_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/two_folders_a_b_lzma2_d1m_ms1f_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    SevenZipStreamsInfo streamsInfo = header.StreamsInfo;

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);

    SevenZipPackInfo packInfo = streamsInfo.PackInfo.Value;
    SevenZipUnpackInfo unpackInfo = streamsInfo.UnpackInfo;

    Assert.Equal(2, unpackInfo.Folders.Length);
    Assert.Equal(2, packInfo.PackSizes.Length);

    for (int i = 0; i < unpackInfo.Folders.Length; i++)
    {
      SevenZipFolder folder = unpackInfo.Folders[i];

      Assert.Single(folder.Coders);
      Assert.True(IsLzma2(folder.Coders[0].MethodId));
      Assert.Single(folder.PackedStreamIndices);
      Assert.Empty(folder.BindPairs);

      Assert.Equal(
          SevenZipFolderDecodeResult.Ok,
          SevenZipFolderDecoder.TryGetFolderPackedStreamRanges(
              streamsInfo,
              reader.PackedStreams.Span,
              folderIndex: i,
              out SevenZipFolderPackedStreamRange[] ranges));

      Assert.Single(ranges);
      Assert.Equal((uint)i, ranges[0].PackStreamIndex);
    }

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

    Assert.Equal(MakeFilled(4096, 0x41), byName["a.bin"].Bytes);
    Assert.Equal(MakeFilled(6000, 0x42), byName["b.bin"].Bytes);
  }

  private static bool IsLzma2(byte[] methodId)
      => methodId.Length == 1 && methodId[0] == 0x21;

  private static byte[] MakeFilled(int length, byte value)
  {
    byte[] bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
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
