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
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

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
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

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
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

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

  [Fact]
  public void ExtractToDirectory_РеальныйAesCopyАрхив_СПаролем_ЗаписываетИсходныйФайл()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword password = SevenZipPassword.FromString(Password);

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, "aes-real.txt");

      Assert.True(File.Exists(filePath));
      Assert.Equal(
          Encoding.UTF8.GetBytes("LzmaSharp AES real 7z test\r\n"),
          File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesCopyАрхив_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    string root = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.False(File.Exists(Path.Combine(root, "aes-real.txt")));
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesCopyАрхив_СНевернымПаролем_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_copy_singlefile_pwd_mhe_off.7z");

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword password = SevenZipPassword.FromString("wrong-password");

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.Equal(archive.Length, bytesConsumed);

      Assert.False(File.Exists(Path.Combine(root, "aes-real.txt")));
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static byte[] ReadTestDataBytes(
      string relativePathFromSevenZipFolder,
      [CallerFilePath] string callerFile = "")
  {
    string dir = Path.GetDirectoryName(callerFile)!;
    string fullPath = Path.GetFullPath(Path.Combine(dir, relativePathFromSevenZipFolder));
    return File.ReadAllBytes(fullPath);
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zAesCopyTests),
        Guid.NewGuid().ToString("N"));
  }

  private static void TryDeleteTree(string path)
  {
    try
    {
      if (Directory.Exists(path))
        Directory.Delete(path, recursive: true);
    }
    catch
    {
      // best-effort cleanup для тестового каталога
    }
  }
}
