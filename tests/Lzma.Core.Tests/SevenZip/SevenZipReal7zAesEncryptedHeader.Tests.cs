using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesEncryptedHeaderTests
{
  private const string _password = "LzmaSharp-AES-Stage15";

  [Fact]
  public void DecodeToEntries_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(_password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeToEntries_РеальныйAesАрхивСЗашифрованнымHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  [Fact]
  public void DecodeSingleFileToArray_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(_password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string fileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    string root = CreateTempRoot();

    try
    {
      using SevenZipPassword password = SevenZipPassword.FromString(_password);

      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.WithPassword(password),
          destinationDirectory: root,
          overwrite: false,
          bytesConsumed: out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, result);
      Assert.Equal(archive.Length, bytesConsumed);
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
        nameof(SevenZipReal7zAesEncryptedHeaderTests),
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
