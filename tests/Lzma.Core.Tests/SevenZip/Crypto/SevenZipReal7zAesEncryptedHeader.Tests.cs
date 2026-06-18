using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesEncryptedHeaderTests
{
  private const string _password = "LzmaSharp-AES-Stage15";

  [Fact]
  public void DecodeToEntries_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетФайл()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(_password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.False(entry.IsDirectory);
    Assert.Equal("aes-mhe-on-real.txt", entry.Name);
    Assert.Equal(CreateExpectedBytes(), entry.Bytes);
  }

  [Fact]
  public void DecodeSingleFileToArray_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетИсходныйФайл()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(_password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        fileBytes: out byte[] fileBytes,
        fileName: out string fileName,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal("aes-mhe-on-real.txt", fileName);
    Assert.Equal(CreateExpectedBytes(), fileBytes);
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ЗаписываетИсходныйФайл()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

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

      Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
      Assert.Equal(archive.Length, bytesConsumed);

      string filePath = Path.Combine(root, "aes-mhe-on-real.txt");

      Assert.True(File.Exists(filePath));
      Assert.Equal(CreateExpectedBytes(), File.ReadAllBytes(filePath));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void DecodeToEntries_РеальныйAesАрхивСЗашифрованнымHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

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
  public void DecodeToEntries_РеальныйAesАрхивСЗашифрованнымHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    using SevenZipPassword password = SevenZipPassword.FromString("wrong-password");

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }

  private static byte[] CreateExpectedBytes()
  {
    return System.Text.Encoding.UTF8.GetBytes(
        "LzmaSharp AES encrypted header real 7z test\r\n");
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
