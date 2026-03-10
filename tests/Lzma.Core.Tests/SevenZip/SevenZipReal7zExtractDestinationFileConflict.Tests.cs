using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractDestinationFileConflictTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ExtractToDirectory_DestinationDirectoryAlreadyFile_InvalidData_AndOriginalFilePreserved(bool overwrite)
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_mhc_off.7z");

    string parent = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractDestinationFileConflictTests),
        Guid.NewGuid().ToString("N"));

    string destinationAsFile = Path.Combine(parent, "dest_as_file");

    try
    {
      Directory.CreateDirectory(parent);

      byte[] original = [1, 2, 3, 4, 5];
      File.WriteAllBytes(destinationAsFile, original);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          destinationAsFile,
          overwrite: overwrite,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Исходный файл должен остаться нетронутым.
      Assert.True(File.Exists(destinationAsFile));
      Assert.False(Directory.Exists(destinationAsFile));
      Assert.Equal(original, File.ReadAllBytes(destinationAsFile));
    }
    finally
    {
      TryDeleteTree(parent);
    }
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

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
