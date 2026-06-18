using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zExtractDirectoriesTests
{
  [Fact]
  public void ExtractToDirectory_Real7z_WithDirectories_AndEmptyEntries_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_lzma2_mhc.7z");

    // 1) DecodeToEntries: проверяем флаги IsDirectory
    SevenZipArchiveDecodeResult r1 = SevenZipArchiveDecoder.DecodeToEntries(
      archive,
      out SevenZipDecodedEntry[] entries,
      out int consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r1);
    Assert.Equal(archive.Length, consumed1);

    var byName = new Dictionary<string, SevenZipDecodedEntry>(StringComparer.Ordinal);
    foreach (var e in entries)
      byName[Norm(e.Name)] = e;

    Assert.True(byName.ContainsKey("dir/hello.bin"));
    Assert.True(byName.ContainsKey("empty.txt"));
    Assert.True(byName.ContainsKey("emptydir"));

    Assert.False(byName["dir/hello.bin"].IsDirectory);
    Assert.Equal(MakePattern(1024, mul: 17, add: 3), byName["dir/hello.bin"].Bytes);

    Assert.False(byName["empty.txt"].IsDirectory);
    Assert.Empty(byName["empty.txt"].Bytes);

    Assert.True(byName["emptydir"].IsDirectory);
    Assert.Empty(byName["emptydir"].Bytes);

    // 2) ExtractToDirectory: проверяем реальное создание на диске
    string root = Path.Combine(Path.GetTempPath(), "LzmaSharpTests", Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r2 = SevenZipArchiveDecoder.ExtractToDirectory(
        archive,
        root,
        overwrite: false,
        out int consumed2);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, r2);
      Assert.Equal(archive.Length, consumed2);

      Assert.True(Directory.Exists(Path.Combine(root, "dir")));
      Assert.True(Directory.Exists(Path.Combine(root, "emptydir")));

      string fileHello = Path.Combine(root, "dir", "hello.bin");
      Assert.True(File.Exists(fileHello));
      Assert.Equal(MakePattern(1024, mul: 17, add: 3), File.ReadAllBytes(fileHello));

      string fileEmpty = Path.Combine(root, "empty.txt");
      Assert.True(File.Exists(fileEmpty));
      Assert.Empty(File.ReadAllBytes(fileEmpty));
    }
    finally
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
  }

  private static string Norm(string name)
  {
    name = name.Replace('\\', '/');
    if (name.EndsWith('/'))
      name = name[..^1];
    return name;
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
