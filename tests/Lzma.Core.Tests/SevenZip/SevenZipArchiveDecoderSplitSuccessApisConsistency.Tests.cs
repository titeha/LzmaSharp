using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSplitSuccessApisConsistencyTests
{
  [Fact]
  public void SplitTwoVolumesConcatenated_PublicApis_Ok()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v10k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v10k_mhc_off.7z.002"]);

    AssertAllSuccessApis(archive);
  }

  [Fact]
  public void SplitThreeVolumesConcatenated_PublicApis_Ok()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003"]);

    AssertAllSuccessApis(archive);
  }

  private static void AssertAllSuccessApis(byte[] archive)
  {
    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Single(files);
    Assert.Equal("hello.bin", files[0].Name.Replace('\\', '/'));
    Assert.Equal(MakeFilled(16 * 1024, 0x41), files[0].Bytes);

    SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed2);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r2);
    Assert.Equal(archive.Length, consumed2);
    Assert.Single(entries);
    Assert.Equal("hello.bin", entries[0].Name.Replace('\\', '/'));
    Assert.False(entries[0].IsDirectory);
    Assert.Equal(MakeFilled(16 * 1024, 0x41), entries[0].Bytes);

    SevenZipArchiveDecodeResult r3 = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int consumed3);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r3);
    Assert.Equal(archive.Length, consumed3);
    Assert.Equal("hello.bin", fileName.Replace('\\', '/'));
    Assert.Equal(MakeFilled(16 * 1024, 0x41), fileBytes);
  }

  private static byte[] ReadAndConcatTestData(params string[] relativePathsFromSevenZipFolder)
  {
    using var ms = new MemoryStream();

    foreach (string path in relativePathsFromSevenZipFolder)
    {
      byte[] bytes = ReadTestDataBytes(path);
      ms.Write(bytes, 0, bytes.Length);
    }

    return ms.ToArray();
  }

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
