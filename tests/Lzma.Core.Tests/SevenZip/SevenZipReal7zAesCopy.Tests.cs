using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesCopyTests
{
  private const string Password = "LzmaSharp-AES-Stage15";

  [Fact]
  public void DecodeSingleFileToArray_РеальныйAesCopyАрхив_СПаролем_ВозвращаетИсходныйФайл()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(Password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string fileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("aes-real.txt", fileName);
    Assert.Equal(
        Encoding.UTF8.GetBytes("LzmaSharp AES real 7z test\r\n"),
        fileBytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_РеальныйAesCopyАрхив_БезПароля_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] fileBytes,
        fileName: out string fileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void DecodeSingleFileToArray_РеальныйAesCopyАрхив_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    using SevenZipPassword password = SevenZipPassword.FromString("wrong-password");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string fileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
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
