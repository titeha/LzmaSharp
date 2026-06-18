using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesEncryptedHeaderNotSupportedTests
{
  [Fact]
  public void ArchiveReader_Real7z_EncryptedHeader_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult r = reader.Read(archive, out int bytesConsumed);

    // Header зашифрован => без поддержки 7zAES reader должен завершаться NotSupported.
    Assert.Equal(SevenZipArchiveReadResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);

    // Для encrypted header ожидаем EncodedHeader.
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);

    // Обычный Header прочитать нельзя.
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);
  }

  [Fact]
  public void DecodeToArray_Real7z_EncryptedHeader_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);
  }

  [Fact]
  public void ExtractToDirectory_Real7z_EncryptedHeader_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("../TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipReal7zAesEncryptedHeaderNotSupportedTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.ExtractToDirectory(
          archive,
          root,
          overwrite: false,
          out int bytesConsumed);

      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
      Assert.Equal(archive.Length, bytesConsumed);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  private static void TryDeleteTree(string root)
  {
    try
    {
      if (Directory.Exists(root))
        Directory.Delete(root, recursive: true);
    }
    catch
    {
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
}
