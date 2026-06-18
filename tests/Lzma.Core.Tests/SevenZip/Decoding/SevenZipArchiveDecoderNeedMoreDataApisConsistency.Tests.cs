using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderNeedMoreDataApisConsistencyTests
{
  [Fact]
  public void PartialSplitVolume_PublicDecodeApis_NeedMoreData()
  {
    byte[] part1 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v10k_mhc_off.7z.001");

    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToArray(
        part1,
        out SevenZipDecodedFile[] files1,
        out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.NeedMoreData, r1);
    Assert.InRange(consumed1, 0, part1.Length);
    Assert.Empty(files1);

    SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        part1,
        out SevenZipDecodedFile[] files2);

    Assert.Equal(SevenZipArchiveDecodeResult.NeedMoreData, r2);
    Assert.Empty(files2);

    SevenZipArchiveDecodeResult r3 = SevenZipArchiveDecoder.DecodeToEntries(
        part1,
        out SevenZipDecodedEntry[] entries,
        out int consumed3);

    Assert.Equal(SevenZipArchiveDecodeResult.NeedMoreData, r3);
    Assert.InRange(consumed3, 0, part1.Length);
    Assert.Empty(entries);

    SevenZipArchiveDecodeResult r4 = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        part1,
        out byte[] fileBytes,
        out string fileName,
        out int consumed4);

    Assert.Equal(SevenZipArchiveDecodeResult.NeedMoreData, r4);
    Assert.InRange(consumed4, 0, part1.Length);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void PartialSplitVolume_ExtractToDirectory_NeedMoreData_AndDoesNotCreateDestination()
  {
    byte[] part1 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v10k_mhc_off.7z.001");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderNeedMoreDataApisConsistencyTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          part1,
          root,
          overwrite: false,
          out int consumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NeedMoreData, r);
      Assert.InRange(consumed, 0, part1.Length);

      // До создания каталогов/файлов дело доходить не должно.
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
    }
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
