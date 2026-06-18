using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDecodeToEntriesTests
{
  [Fact]
  public void DecodeToEntries_Real7z_Directories_EmptyFile_MetadataArchive_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_meta_lzma2_mhc.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.True(entries.Length >= 3);

    var byName = new Dictionary<string, SevenZipDecodedEntry>(StringComparer.Ordinal);
    foreach (SevenZipDecodedEntry e in entries)
      byName.Add(e.Name.Replace('\\', '/'), e);

    Assert.True(byName.ContainsKey("dir/hello.bin"));
    Assert.True(byName.ContainsKey("empty.txt"));
    Assert.True(byName.ContainsKey("emptydir"));

    Assert.False(byName["dir/hello.bin"].IsDirectory);
    Assert.Equal(MakePattern(1024, mul: 17, add: 3), byName["dir/hello.bin"].Bytes);

    Assert.False(byName["empty.txt"].IsDirectory);
    Assert.Empty(byName["empty.txt"].Bytes);

    Assert.True(byName["emptydir"].IsDirectory);
    Assert.Empty(byName["emptydir"].Bytes);
  }

  private static byte[] MakePattern(int length, int mul, int add)
  {
    var bytes = new byte[length];
    for (int i = 0; i < bytes.Length; i++)
      bytes[i] = unchecked((byte)(i * mul + add));

    return bytes;
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
