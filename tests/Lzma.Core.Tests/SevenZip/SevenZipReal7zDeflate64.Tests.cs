using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDeflate64Tests
{
  [Fact]
  public void DecodeToArray_Real7z_Deflate64_SingleFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate64_singlefile_mhc.7z");

    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.True(
        reader.NextHeaderKind == SevenZipNextHeaderKind.Header ||
        reader.NextHeaderKind == SevenZipNextHeaderKind.EncodedHeader);

    SevenZipFolder folder = reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders[0];

    Assert.Single(folder.Coders);
    Assert.True(IsDeflate64(folder.Coders[0].MethodId));

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] files,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);
    Assert.Equal("deflate64.bin", files[0].Name.Replace('\\', '/'));

    byte[] expected = new byte[16 * 1024];
    expected.AsSpan().Fill(0x41);

    Assert.Equal(expected, files[0].Bytes);
  }

  private static bool IsDeflate64(byte[] methodId)
  {
    return methodId.Length == 3
        && methodId[0] == 0x04
        && methodId[1] == 0x01
        && methodId[2] == 0x09;
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
