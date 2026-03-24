using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zSplitVolumesWrongOrderTests
{
  [Fact]
  public void ArchiveReader_Real7z_Split3VolumesWrongOrder_InvalidData()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002"]);

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, r);
    Assert.InRange(bytesConsumed, 0, archive.Length);
  }

  [Fact]
  public void DecodeToArray_Real7z_Split3VolumesWrongOrder_InvalidData()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002"]);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, r);
    Assert.InRange(bytesConsumed, 0, archive.Length);
  }

  [Fact]
  public void DecodeToArray_Real7z_Split3VolumesConcatenated_Ok()
  {
    byte[] archive = ReadAndConcatTestData(
        ["TestData/Real/hello_copy_split_v6k_mhc_off.7z.001",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.002",
        "TestData/Real/hello_copy_split_v6k_mhc_off.7z.003"]);

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("hello.bin", files[0].Name.Replace('\\', '/'));

    byte[] expected = new byte[16 * 1024];
    expected.AsSpan().Fill(0x41);

    Assert.Equal(expected, files[0].Bytes);
  }

  private static byte[] ReadAndConcatTestData(params string[] relativePathsFromSevenZipFolder)
  {
    using var ms = new MemoryStream();

    foreach (string path in relativePathsFromSevenZipFolder)
    {
      byte[] bytes = ReadTestDataBytes(path);
      ms.Write(bytes, 0, bytes.Length);
    }

    return ms.ToArray();
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
