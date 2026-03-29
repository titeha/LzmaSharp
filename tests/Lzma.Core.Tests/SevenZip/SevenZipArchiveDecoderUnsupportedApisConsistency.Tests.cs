using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderUnsupportedApisConsistencyTests
{
  [Fact]
  public void EncryptedHeader_AllPublicApis_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_7zaes_mhe_on_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult read = reader.Read(archive, out int readConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, read);
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeToArray(archive, out _, out int consumed1));
    Assert.Equal(archive.Length, consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] allFiles));
    Assert.Empty(allFiles);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries, out int consumed2));
    Assert.Equal(archive.Length, consumed2);
    Assert.Empty(entries);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeSingleFileToArray(archive, out byte[] fileBytes, out string fileName, out int consumed3));
    Assert.Equal(archive.Length, consumed3);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderUnsupportedApisConsistencyTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
          SevenZipArchiveDecoder.ExtractToDirectory(archive, root, overwrite: false, out int consumed4));
      Assert.Equal(archive.Length, consumed4);
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void EncryptedData_HeaderVisible_PublicDecoders_NotSupported_ButReaderOk()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_7zaes_mhc_off.7z");

    var reader = new SevenZipArchiveReader();
    SevenZipArchiveReadResult read = reader.Read(archive, out int readConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, read);
    Assert.Equal(archive.Length, readConsumed);
    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.Header.HasValue);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeToArray(archive, out _, out int consumed1));
    Assert.Equal(archive.Length, consumed1);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeAllFilesToArray(archive, out SevenZipDecodedFile[] allFiles));
    Assert.Empty(allFiles);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries, out int consumed2));
    Assert.Equal(archive.Length, consumed2);
    Assert.Empty(entries);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveDecoder.DecodeSingleFileToArray(archive, out byte[] fileBytes, out string fileName, out int consumed3));
    Assert.Equal(archive.Length, consumed3);
    Assert.Empty(fileBytes);
    Assert.Equal(string.Empty, fileName);

    string root = Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderUnsupportedApisConsistencyTests),
        Guid.NewGuid().ToString("N"));

    try
    {
      Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
          SevenZipArchiveDecoder.ExtractToDirectory(archive, root, overwrite: false, out int consumed4));
      Assert.Equal(archive.Length, consumed4);
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
