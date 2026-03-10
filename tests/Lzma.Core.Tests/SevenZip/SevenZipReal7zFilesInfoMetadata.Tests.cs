using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zFilesInfoMetadataTests
{
  [Fact]
  public void Read_Real7z_FilesInfo_MTime_And_WinAttrib_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/dir_emptyfile_emptydir_meta_lzma2_mhc.7z");

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

    Assert.NotNull(fi.EmptyFiles);
    Assert.Equal(fileCount, fi.EmptyFiles!.Length);

    Assert.NotNull(fi.MTimeDefined);
    Assert.NotNull(fi.MTime);
    Assert.Equal(fileCount, fi.MTimeDefined!.Length);
    Assert.Equal(fileCount, fi.MTime!.Length);

    Assert.NotNull(fi.WinAttribDefined);
    Assert.NotNull(fi.WinAttrib);
    Assert.Equal(fileCount, fi.WinAttribDefined!.Length);
    Assert.Equal(fileCount, fi.WinAttrib!.Length);

    var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
    for (int i = 0; i < fileCount; i++)
      indexByName[Norm(fi.Names[i])] = i;

    Assert.True(indexByName.ContainsKey("dir/hello.bin"));
    Assert.True(indexByName.ContainsKey("empty.txt"));
    Assert.True(indexByName.ContainsKey("emptydir"));

    int iHello = indexByName["dir/hello.bin"];
    int iEmptyFile = indexByName["empty.txt"];
    int iEmptyDir = indexByName["emptydir"];

    // Пустые/непустые элементы.
    Assert.False(fi.EmptyStreams[iHello]);
    Assert.True(fi.EmptyStreams[iEmptyFile]);
    Assert.True(fi.EmptyFiles[iEmptyFile]);

    Assert.True(fi.EmptyStreams[iEmptyDir]);
    Assert.False(fi.EmptyFiles[iEmptyDir]);

    // MTime: сравниваем raw FILETIME.
    ulong helloMTime = checked((ulong)new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc).ToFileTimeUtc());
    ulong emptyFileMTime = checked((ulong)new DateTime(2023, 04, 03, 02, 01, 00, DateTimeKind.Utc).ToFileTimeUtc());
    ulong emptyDirMTime = checked((ulong)new DateTime(2022, 11, 10, 09, 08, 07, DateTimeKind.Utc).ToFileTimeUtc());

    Assert.True(fi.MTimeDefined[iHello]);
    Assert.Equal(helloMTime, fi.MTime[iHello]);

    Assert.True(fi.MTimeDefined[iEmptyFile]);
    Assert.Equal(emptyFileMTime, fi.MTime[iEmptyFile]);

    Assert.True(fi.MTimeDefined[iEmptyDir]);
    Assert.Equal(emptyDirMTime, fi.MTime[iEmptyDir]);

    // WinAttrib: проверяем только значимые биты, а не полное точное число.
    Assert.True(fi.WinAttribDefined[iHello]);
    Assert.True(fi.WinAttribDefined[iEmptyFile]);
    Assert.True(fi.WinAttribDefined[iEmptyDir]);

    FileAttributes helloAttrs = (FileAttributes)fi.WinAttrib[iHello];
    FileAttributes emptyFileAttrs = (FileAttributes)fi.WinAttrib[iEmptyFile];
    FileAttributes emptyDirAttrs = (FileAttributes)fi.WinAttrib[iEmptyDir];

    Assert.NotEqual(0, (int)(helloAttrs & FileAttributes.ReadOnly));
    Assert.Equal(0, (int)(helloAttrs & FileAttributes.Directory));

    Assert.NotEqual(0, (int)(emptyFileAttrs & FileAttributes.Hidden));
    Assert.Equal(0, (int)(emptyFileAttrs & FileAttributes.Directory));

    Assert.NotEqual(0, (int)(emptyDirAttrs & FileAttributes.Hidden));
    Assert.NotEqual(0, (int)(emptyDirAttrs & FileAttributes.Directory));
  }

  private static string Norm(string name)
  {
    name = name.Replace('\\', '/');
    if (name.EndsWith('/'))
      name = name[..^1];

    return name;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
