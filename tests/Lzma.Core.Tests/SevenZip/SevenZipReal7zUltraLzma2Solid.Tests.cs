using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zUltraLzma2SolidTests
{
  [Fact]
  public void DecodeAndExtract_Real7z_Ultra_Lzma2_Solid_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/ultra_a70m_empty_lzma2_d64m_solid_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(
        reader.NextHeaderKind == SevenZipNextHeaderKind.Header ||
        reader.NextHeaderKind == SevenZipNextHeaderKind.EncodedHeader);

    SevenZipHeader header = reader.Header!.Value;

    Assert.NotNull(header.StreamsInfo.UnpackInfo);
    Assert.NotEmpty(header.StreamsInfo.UnpackInfo!.Folders);
    Assert.Contains(
        header.StreamsInfo.UnpackInfo.Folders,
        static f => f.Coders.Length == 1 && IsLzma2(f.Coders[0].MethodId));

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Equal((int)header.FilesInfo.FileCount, files.Length);
    Assert.Contains(files, static f => f.Bytes.Length == 0);
    Assert.Contains(files, static f => f.Bytes.Length >= 64 * 1024 * 1024);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zUltraLzma2SolidTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult extractResult = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int extractConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, extractResult);
      Assert.Equal(archive.Length, extractConsumed);

      foreach (SevenZipDecodedFile f in files)
      {
        string path = Path.Combine(
            root,
            f.Name.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"Не найден извлечённый файл: {path}");

        var fi = new FileInfo(path);
        Assert.Equal(f.Bytes.Length, fi.Length);

        AssertFileStartsAndEndsWith(path, f.Bytes);
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static bool IsLzma2(byte[] methodId)
      => methodId.Length == 1 && methodId[0] == 0x21;

  private static void AssertFileStartsAndEndsWith(string path, byte[] expected)
  {
    int probe = Math.Min(64, expected.Length);

    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    byte[] head = new byte[probe];
    int headRead = fs.Read(head, 0, probe);
    Assert.Equal(probe, headRead);
    Assert.Equal(expected.AsSpan(0, probe).ToArray(), head);

    if (expected.Length == 0)
      return;

    byte[] tail = new byte[probe];
    fs.Seek(-probe, SeekOrigin.End);
    int tailRead = fs.Read(tail, 0, probe);
    Assert.Equal(probe, tailRead);
    Assert.Equal(expected.AsSpan(expected.Length - probe, probe).ToArray(), tail);
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
