using System.Runtime.CompilerServices;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesLzma2MultiFileTests
{
  private const string _password = "LzmaSharp-AES-Stage15";

  [Fact]
  public void DecodeToEntries_РеальныйAesLzma2MultiFileАрхив_СПаролем_ВозвращаетФайлы()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

    using SevenZipPassword password = SevenZipPassword.FromString(_password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        entries: out SevenZipDecodedEntry[] entries,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);

    Dictionary<string, byte[]> actualFiles = CollectFiles(entries);
    Dictionary<string, byte[]> expectedFiles = CreateExpectedFiles();

    Assert.Equal(expectedFiles.Count, actualFiles.Count);

    foreach ((string name, byte[] expectedBytes) in expectedFiles)
    {
      Assert.True(actualFiles.TryGetValue(name, out byte[]? actualBytes), $"Файл не найден: {name}");
      Assert.Equal(expectedBytes, actualBytes);
    }
  }

  [Fact]
  public void DecodeToEntries_РеальныйAesLzma2MultiFileАрхив_БезПароля_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

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
  public void DecodeToEntries_РеальныйAesLzma2MultiFileАрхив_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

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

  [Fact]
  public void ExtractToDirectory_РеальныйAesLzma2MultiFileАрхив_СПаролем_ЗаписываетФайлы()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

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

      Dictionary<string, byte[]> expectedFiles = CreateExpectedFiles();

      foreach ((string relativeName, byte[] expectedBytes) in expectedFiles)
      {
        string filePath = Path.Combine(root, relativeName.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(filePath), $"Файл не найден: {filePath}");
        Assert.Equal(expectedBytes, File.ReadAllBytes(filePath));
      }
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesLzma2MultiFileАрхив_БезПароля_ВозвращаетNotSupportedИНичегоНеПишет()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

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
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ExtractToDirectory_РеальныйAesLzma2MultiFileАрхив_СНевернымПаролем_ВозвращаетInvalidDataИНичегоНеПишет()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/aes_lzma2_multifile_pwd_mhe_off.7z");

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
      Assert.False(Directory.Exists(root));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static Dictionary<string, byte[]> CreateExpectedFiles()
  {
    return new Dictionary<string, byte[]>(StringComparer.Ordinal)
    {
      ["file-one.txt"] = Encoding.UTF8.GetBytes("LzmaSharp AES LZMA2 multi-file test: file one\r\n"),
      ["nested/file-two.txt"] = Encoding.UTF8.GetBytes(
          "LzmaSharp AES LZMA2 multi-file test: file two\r\n"
        + "Nested directory payload\r\n"),
      ["empty.bin"] = [],
    };
  }

  private static Dictionary<string, byte[]> CollectFiles(SevenZipDecodedEntry[] entries)
  {
    var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

    foreach (SevenZipDecodedEntry entry in entries)
    {
      if (entry.IsDirectory)
        continue;

      files[NormalizeEntryName(entry.Name)] = entry.Bytes;
    }

    return files;
  }

  private static string NormalizeEntryName(string name)
  {
    return name.Replace('\\', '/');
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
        nameof(SevenZipReal7zAesLzma2MultiFileTests),
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
