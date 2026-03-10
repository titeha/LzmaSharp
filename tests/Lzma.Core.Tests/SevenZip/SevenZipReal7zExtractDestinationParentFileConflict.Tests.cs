using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractDestinationParentFileConflictTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ExtractToDirectory_DestinationParentIsFile_InvalidData_AndOriginalFilePreserved(bool overwrite)
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_mhc_off.7z");

    string tempRoot = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractDestinationParentFileConflictTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(tempRoot);

      // Делаем файл, который будет родительским сегментом destinationDirectory.
      string parentFilePath = Path.Combine(tempRoot, "parent_as_file");
      byte[] original = [1, 2, 3, 4, 5];
      File.WriteAllBytes(parentFilePath, original);

      string destinationDirectory = Path.Combine(parentFilePath, "dest");

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          destinationDirectory,
          overwrite: overwrite,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Исходный файл должен остаться нетронутым.
      Assert.True(File.Exists(parentFilePath));
      Assert.False(Directory.Exists(parentFilePath));
      Assert.Equal(original, File.ReadAllBytes(parentFilePath));

      // Каталог назначения не должен появиться.
      Assert.False(Directory.Exists(destinationDirectory));
    }
    finally
    {
      TryDeleteTree(tempRoot);
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
