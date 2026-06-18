using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractOverwriteMetadataTests
{
  [Fact]
  public void ExtractToDirectory_Real7z_MetadataArchive_OverwriteTrue_ReplacesContent_AndReappliesMetadata()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_meta_lzma2_mhc.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zExtractOverwriteMetadataTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Directory.CreateDirectory(root);

      string dirPath = Path.Combine(root, "dir");
      string helloPath = Path.Combine(dirPath, "hello.bin");
      string emptyFilePath = Path.Combine(root, "empty.txt");
      string emptyDirPath = Path.Combine(root, "emptydir");

      Directory.CreateDirectory(dirPath);
      Directory.CreateDirectory(emptyDirPath);

      // Предзаполняем конфликтующим содержимым и "неправильными" временами.
      File.WriteAllBytes(helloPath, MakeFilled(123, 0x55));
      File.WriteAllBytes(emptyFilePath, new byte[] { 1, 2, 3, 4 });

      File.SetLastWriteTimeUtc(helloPath, new DateTime(2010, 01, 02, 03, 04, 05, DateTimeKind.Utc));
      File.SetLastWriteTimeUtc(emptyFilePath, new DateTime(2011, 02, 03, 04, 05, 06, DateTimeKind.Utc));
      Directory.SetLastWriteTimeUtc(emptyDirPath, new DateTime(2012, 03, 04, 05, 06, 07, DateTimeKind.Utc));

      if (OperatingSystem.IsWindows())
      {
        // hello.bin делаем read-only, чтобы заодно пройтись по ветке overwrite+снятие атрибутов.
        File.SetAttributes(helloPath, FileAttributes.ReadOnly | FileAttributes.Archive);

        // empty.txt делаем обычным/не тем, чем он должен стать после извлечения.
        File.SetAttributes(emptyFilePath, FileAttributes.Archive);

        // emptydir без Hidden, чтобы проверить повторное применение archive metadata.
        File.SetAttributes(emptyDirPath, FileAttributes.Directory);
      }

      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: true,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.True(Directory.Exists(dirPath));
      Assert.True(File.Exists(helloPath));
      Assert.True(File.Exists(emptyFilePath));
      Assert.True(Directory.Exists(emptyDirPath));

      Assert.Equal(MakePattern(1024, mul: 17, add: 3), File.ReadAllBytes(helloPath));
      Assert.Empty(File.ReadAllBytes(emptyFilePath));

      AssertUtcClose(
          new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc),
          File.GetLastWriteTimeUtc(helloPath));

      AssertUtcClose(
          new DateTime(2023, 04, 03, 02, 01, 00, DateTimeKind.Utc),
          File.GetLastWriteTimeUtc(emptyFilePath));

      AssertUtcClose(
          new DateTime(2022, 11, 10, 09, 08, 07, DateTimeKind.Utc),
          Directory.GetLastWriteTimeUtc(emptyDirPath));

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

  private static byte[] MakeFilled(int length, byte value)
  {
    byte[] bytes = new byte[length];
    bytes.AsSpan().Fill(value);
    return bytes;
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

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
