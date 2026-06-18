using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDirectoriesAndEmptyFilesTests
{
  [Fact]
  public void DecodeToArray_Real7z_Dir_EmptyFile_EmptyDir_Ok_AndFlagsParsed()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/dir_emptyfile_emptydir_lzma2_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    SevenZipHeader header = reader.Header!.Value;
    SevenZipFilesInfo fi = header.FilesInfo;

    Assert.True(fi.FileCount > 0);
    Assert.NotNull(fi.Names);

    int fileCount = checked((int)fi.FileCount);
    Assert.Equal(fileCount, fi.Names!.Length);

    Assert.NotNull(fi.EmptyStreams);
    Assert.Equal(fileCount, fi.EmptyStreams!.Length);

    // Ожидаем, что kEmptyFile присутствует, потому что у нас есть и пустой файл, и каталог.
    Assert.NotNull(fi.EmptyFiles);
    Assert.Equal(fileCount, fi.EmptyFiles!.Length);

    // Ищем индексы по именам (нормализуем разделители и убираем хвостовой '/').
    var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);

    for (int i = 0; i < fileCount; i++)
      indexByName[Norm(fi.Names[i])] = i;

    Assert.True(indexByName.ContainsKey("dir/hello.bin"));
    Assert.True(indexByName.ContainsKey("empty.txt"));
    Assert.True(indexByName.ContainsKey("emptydir"));

    int iHello = indexByName["dir/hello.bin"];
    int iEmptyFile = indexByName["empty.txt"];
    int iEmptyDir = indexByName["emptydir"];

    Assert.False(fi.EmptyStreams[iHello]);

    Assert.True(fi.EmptyStreams[iEmptyFile]);
    Assert.True(fi.EmptyFiles[iEmptyFile]);   // пустой файл

    Assert.True(fi.EmptyStreams[iEmptyDir]);
    Assert.False(fi.EmptyFiles[iEmptyDir]);   // каталог

    // Decode
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    var byName = new Dictionary<string, SevenZipDecodedFile>(StringComparer.Ordinal);
    foreach (var f in files)
      byName[Norm(f.Name)] = f;

    Assert.True(byName.ContainsKey("dir/hello.bin"));
    Assert.True(byName.ContainsKey("empty.txt"));
    Assert.True(byName.ContainsKey("emptydir"));

    Assert.Equal(MakePattern(1024, mul: 17, add: 3), byName["dir/hello.bin"].Bytes);
    Assert.Empty(byName["empty.txt"].Bytes);
    Assert.Empty(byName["emptydir"].Bytes);
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
