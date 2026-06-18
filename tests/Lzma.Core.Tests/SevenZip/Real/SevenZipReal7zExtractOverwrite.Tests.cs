using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractOverwriteTests
{
  [Fact]
  public void ExtractToDirectory_ExistingFile_OverwriteFalse_InvalidData_AndOriginalFilePreserved()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_mhc_off.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractOverwriteTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      string fullPath = Path.Combine(root, "hello.bin");
      byte[] original = MakeFilled(123, 0x55);
      File.WriteAllBytes(fullPath, original);

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
      Assert.Equal(archive.Length, bytesConsumed);

      // Существующий файл не должен быть перезаписан.
      Assert.Equal(original, File.ReadAllBytes(fullPath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_ExistingFile_OverwriteTrue_Ok_AndFileReplaced()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_mhc_off.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractOverwriteTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      string fullPath = Path.Combine(root, "hello.bin");
      File.WriteAllBytes(fullPath, MakeFilled(321, 0x22));

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: true,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      byte[] expected = MakeFilled(16 * 1024, 0x41);
      Assert.Equal(expected, File.ReadAllBytes(fullPath));
    }
    finally
    {
      TryDeleteTree(root);
    }
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

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
