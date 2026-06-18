using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractOverwriteReadOnlyTests
{
  [Fact]
  public void ExtractToDirectory_ExistingReadOnlyFile_OverwriteTrue_Ok_AndFileReplaced()
  {
    if (!OperatingSystem.IsWindows())
      return;

    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_mhc_off.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractOverwriteReadOnlyTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      string fullPath = Path.Combine(root, "hello.bin");

      File.WriteAllBytes(fullPath, MakeFilled(123, 0x55));
      File.SetAttributes(fullPath, FileAttributes.ReadOnly);

      FileAttributes before = File.GetAttributes(fullPath);
      Assert.NotEqual(0, (int)(before & FileAttributes.ReadOnly));

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: true,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.Equal(MakeFilled(16 * 1024, 0x41), File.ReadAllBytes(fullPath));

      FileAttributes after = File.GetAttributes(fullPath);
      Assert.Equal(0, (int)(after & FileAttributes.ReadOnly));
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
