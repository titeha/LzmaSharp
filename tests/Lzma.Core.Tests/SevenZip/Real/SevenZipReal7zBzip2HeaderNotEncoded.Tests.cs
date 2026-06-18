using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zBzip2HeaderNotEncodedTests
{
  [Fact]
  public void DecodeToArray_Real7z_BZip2_HeaderNotEncoded_Ok()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/bzip2_singlefile_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(reader.Header.HasValue);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);

    SevenZipFolder folder = reader.Header.Value.StreamsInfo.UnpackInfo!.Folders[0];
    Assert.Single(folder.Coders);
    Assert.True(IsBZip2(folder.Coders[0].MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("bzip2.bin", files[0].Name.Replace('\\', '/'));

    byte[] expected = new byte[32 * 1024];
    expected.AsSpan().Fill(0x41);

    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsBZip2(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x02
        && methodId[2] == 0x02;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
