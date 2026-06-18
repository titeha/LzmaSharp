using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractParentPathConflictsTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void ExtractToDirectory_TargetHasFileOnParentPath_InvalidData_AndOriginalFilePreserved(bool overwrite)
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_lzma2_mhc.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractParentPathConflictsTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      // В архиве есть "dir/hello.bin".
      // На диске заранее делаем ФАЙЛ "dir", то есть конфликтуем по родительскому сегменту.
      string parentFilePath = Path.Combine(root, "dir");
      byte[] original = [1, 2, 3, 4, 5];
      File.WriteAllBytes(parentFilePath, original);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: overwrite,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Исходный файл должен остаться файлом и не быть тронутым.
      Assert.True(File.Exists(parentFilePath));
      Assert.False(Directory.Exists(parentFilePath));
      Assert.Equal(original, File.ReadAllBytes(parentFilePath));

      // Вложенный файл не должен появиться.
      Assert.False(File.Exists(Path.Combine(root, "dir", "hello.bin")));
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
