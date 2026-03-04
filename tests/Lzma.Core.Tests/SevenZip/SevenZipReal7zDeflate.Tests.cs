using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zDeflateTests
{
  [Fact]
  public void DecodeToArray_Real7z_Deflate_SingleFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/deflate_singlefile_mhc.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);

    // Имя фиксируем, чтобы не “проглотить” случайно другой архив.
    Assert.Equal("deflate.bin", files[0].Name.Replace('\\', '/'));

    byte[] expected = new byte[16 * 1024];
    for (int i = 0; i < expected.Length; i++)
      expected[i] = 0x41;

    Assert.Equal(expected, files[0].Bytes);
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
