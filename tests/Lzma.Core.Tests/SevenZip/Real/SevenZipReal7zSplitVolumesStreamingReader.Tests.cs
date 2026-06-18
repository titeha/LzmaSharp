using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zSplitVolumesStreamingReaderTests
{
  [Fact]
  public void Read_Real7z_SplitTwoVolumes_Part1ThenPart2_Ok()
  {
    byte[] part1 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v10k_mhc_off.7z.001");
    byte[] part2 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v10k_mhc_off.7z.002");

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult r1 = reader.Read(part1, out int consumed1);
    Assert.Equal(SevenZipArchiveReadResult.NeedMoreInput, r1);
    Assert.Equal(part1.Length, consumed1);
    Assert.False(reader.Header.HasValue);

    SevenZipArchiveReadResult r2 = reader.Read(part2, out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.Ok, r2);
    Assert.Equal(part2.Length, consumed2);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    Assert.Equal(1u, header.FilesInfo.FileCount);
    Assert.NotNull(header.FilesInfo.Names);
    Assert.Single(header.FilesInfo.Names!);
    Assert.Equal("hello.bin", header.FilesInfo.Names![0].Replace('\\', '/'));

    // После перехода в Ok reader должен оставаться в терминальном состоянии.
    SevenZipArchiveReadResult r3 = reader.Read(ReadOnlySpan<byte>.Empty, out int consumed3);
    Assert.Equal(SevenZipArchiveReadResult.Ok, r3);
    Assert.Equal(0, consumed3);
  }

  [Fact]
  public void Read_Real7z_SplitThreeVolumes_Part1ThenPart2ThenPart3_Ok()
  {
    byte[] part1 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v6k_mhc_off.7z.001");
    byte[] part2 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v6k_mhc_off.7z.002");
    byte[] part3 = ReadTestDataBytes("../TestData/Real/hello_copy_split_v6k_mhc_off.7z.003");

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult r1 = reader.Read(part1, out int consumed1);
    Assert.Equal(SevenZipArchiveReadResult.NeedMoreInput, r1);
    Assert.Equal(part1.Length, consumed1);
    Assert.False(reader.Header.HasValue);

    SevenZipArchiveReadResult r2 = reader.Read(part2, out int consumed2);
    Assert.Equal(SevenZipArchiveReadResult.NeedMoreInput, r2);
    Assert.Equal(part2.Length, consumed2);
    Assert.False(reader.Header.HasValue);

    SevenZipArchiveReadResult r3 = reader.Read(part3, out int consumed3);
    Assert.Equal(SevenZipArchiveReadResult.Ok, r3);
    Assert.Equal(part3.Length, consumed3);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    SevenZipHeader header = reader.Header.Value;
    Assert.Equal(1u, header.FilesInfo.FileCount);
    Assert.NotNull(header.FilesInfo.Names);
    Assert.Single(header.FilesInfo.Names!);
    Assert.Equal("hello.bin", header.FilesInfo.Names![0].Replace('\\', '/'));

    SevenZipArchiveReadResult r4 = reader.Read(ReadOnlySpan<byte>.Empty, out int consumed4);
    Assert.Equal(SevenZipArchiveReadResult.Ok, r4);
    Assert.Equal(0, consumed4);
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
