using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractTypeConflictsTests
{
  [Fact]
  public void ExtractToDirectory_ArchiveFile_TargetHasDirectoryWithSameName_InvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_mhc_off.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractTypeConflictsTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      // В архиве hello.bin — файл.
      // На диске заранее делаем каталог с тем же именем.
      string conflictingDir = Path.Combine(root, "hello.bin");
      Directory.CreateDirectory(conflictingDir);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Каталог должен остаться каталогом.
      Assert.True(Directory.Exists(conflictingDir));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_ArchiveDirectory_TargetHasFileWithSameName_InvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/dir_emptyfile_emptydir_lzma2_mhc.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractTypeConflictsTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      // В архиве emptydir — каталог.
      // На диске заранее делаем файл с тем же именем.
      string conflictingFile = Path.Combine(root, "emptydir");
      File.WriteAllBytes(conflictingFile, [1, 2, 3]);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Файл должен остаться файлом и не замениться каталогом.
      Assert.True(File.Exists(conflictingFile));
      Assert.False(Directory.Exists(conflictingFile));
      Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(conflictingFile));
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
