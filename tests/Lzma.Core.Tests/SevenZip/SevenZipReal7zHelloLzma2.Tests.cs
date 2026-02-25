using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zHelloLzma2Tests
{
  [Fact]
  public void DecodeToArray_Real7z_7ZipLzma2_OneFile_Ok()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_lzma2_d1m_mhc.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
      archive,
      out SevenZipDecodedFile[] files,
      out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(archive.Length, bytesConsumed);

    Assert.Single(files);

    Assert.Equal("hello.bin", files[0].Name);

    byte[] expected = new byte[16384];
    Array.Fill(expected, (byte)0x41);
    Assert.Equal(expected, files[0].Bytes);
  }

  private static byte[] ReadTestDataBytes(string relativePathFromSevenZipFolder, [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }
}
