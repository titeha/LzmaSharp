using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderWrongOrderSplitApisConsistencyTests
{
  [Fact]
  public void WrongOrderSplit_PublicDecodeApis_InvalidData()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002"]);

    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeAllFilesToArray(
        archive,
        out SevenZipDecodedFile[] files);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r1);
    Assert.Empty(files);

    SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int consumed2);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r2);
    Assert.InRange(consumed2, 0, archive.Length);
    Assert.Empty(entries);

    SevenZipArchiveDecodeResult r3 = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] fileBytes,
        out string fileName,
        out int consumed3);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r3);
    Assert.InRange(consumed3, 0, archive.Length);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void WrongOrderSplit_ExtractToDirectory_InvalidData_AndDoesNotCreateDestination()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002"]);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderWrongOrderSplitApisConsistencyTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int consumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.InRange(consumed, 0, archive.Length);

      // На InvalidData из-за неправильного порядка томов до создания destination доходить не должно.
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
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
