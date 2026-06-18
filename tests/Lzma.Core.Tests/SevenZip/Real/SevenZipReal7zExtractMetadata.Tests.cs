using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractMetadataTests
{
  [Fact]
  public void ExtractToDirectory_Real7z_WithMetadata_AppliesMTime_And_WinAttrib()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_meta_lzma2_mhc.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractMetadataTests),
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

      string dirPath = Path.Combine(root, "dir");
      string helloPath = Path.Combine(dirPath, "hello.bin");
      string emptyFilePath = Path.Combine(root, "empty.txt");
      string emptyDirPath = Path.Combine(root, "emptydir");

      Assert.True(Directory.Exists(dirPath));
      Assert.True(File.Exists(helloPath));
      Assert.True(File.Exists(emptyFilePath));
      Assert.True(Directory.Exists(emptyDirPath));

      Assert.Equal(MakePattern(1024, mul: 17, add: 3), File.ReadAllBytes(helloPath));
      Assert.Empty(File.ReadAllBytes(emptyFilePath));

      // Проверяем MTime. Сравниваем с небольшим допуском на файловую систему.
      AssertUtcClose(
          new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc),
          File.GetLastWriteTimeUtc(helloPath));

      AssertUtcClose(
          new DateTime(2023, 04, 03, 02, 01, 00, DateTimeKind.Utc),
          File.GetLastWriteTimeUtc(emptyFilePath));

      AssertUtcClose(
          new DateTime(2022, 11, 10, 09, 08, 07, DateTimeKind.Utc),
          Directory.GetLastWriteTimeUtc(emptyDirPath));

      // WinAttrib код применяет только на Windows.
      if (OperatingSystem.IsWindows())
      {
        FileAttributes helloAttrs = File.GetAttributes(helloPath);
        FileAttributes emptyFileAttrs = File.GetAttributes(emptyFilePath);
        FileAttributes emptyDirAttrs = File.GetAttributes(emptyDirPath);

        Assert.NotEqual(0, (int)(helloAttrs & FileAttributes.ReadOnly));
        Assert.Equal(0, (int)(helloAttrs & FileAttributes.Directory));

        Assert.NotEqual(0, (int)(emptyFileAttrs & FileAttributes.Hidden));
        Assert.Equal(0, (int)(emptyFileAttrs & FileAttributes.Directory));

        Assert.NotEqual(0, (int)(emptyDirAttrs & FileAttributes.Hidden));
        Assert.NotEqual(0, (int)(emptyDirAttrs & FileAttributes.Directory));
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static void AssertUtcClose(DateTime expectedUtc, DateTime actualUtc)
  {
    TimeSpan delta = (actualUtc - expectedUtc).Duration();

    Assert.True(
        delta <= TimeSpan.FromSeconds(2),
        $"Ожидали UTC-время около {expectedUtc:o}, получили {actualUtc:o}, delta={delta}.");
  }

  private static void TryDeleteTree(string root)
  {
    if (!Directory.Exists(root))
      return;

    try
    {
      // На Windows hidden/read-only может мешать удалению.
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
      // Если не смогли очистить атрибуты — всё равно попробуем удалить.
    }

    try
    {
      Directory.Delete(root, recursive: true);
    }
    catch
    {
      // Если удаление не удалось, тест уже завершён; хвост можно убрать вручную.
    }
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
