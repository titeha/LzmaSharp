using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zSplitVolumesExtractTests
{
  [Fact]
  public void ExtractToDirectory_Real7z_SplitTwoVolumesConcatenated_Ok()
  {
    byte[] archive = ReadAndConcatTestData(
        ["../TestData/Real/hello_copy_split_v10k_mhc_off.7z.001",
        "../TestData/Real/hello_copy_split_v10k_mhc_off.7z.002"]);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zSplitVolumesExtractTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, "hello.bin");
      Assert.True(File.Exists(filePath));
      Assert.Equal(MakeFilled(16 * 1024, 0x41), File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_Real7z_SplitThreeVolumesConcatenated_Ok()
  {
    byte[] archive = ReadAndConcatTestData(
        ["../TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "../TestData/Real/hello_copy_split_v6k_mhc_off.7z.002",
        "../TestData/Real/hello_copy_split_v6k_mhc_off.7z.003"]);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zSplitVolumesExtractTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, "hello.bin");
      Assert.True(File.Exists(filePath));
      Assert.Equal(MakeFilled(16 * 1024, 0x41), File.ReadAllBytes(filePath));
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

  private static byte[] MakeFilled(int length, byte value)
  {
    byte[] bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (!Directory.Exists(root))
        return;

      foreach (string filePath in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        File.SetAttributes(filePath, FileAttributes.Normal);

      string[] dirs = Directory.GetDirectories(root, "*", SearchOption.AllDirectories);
      Array.Sort(dirs, static (a, b) => b.Length.CompareTo(a.Length));

      foreach (string dirPath in dirs)
        File.SetAttributes(dirPath, FileAttributes.Directory);

      File.SetAttributes(root, FileAttributes.Directory);
    }
    catch
    {
    }

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
