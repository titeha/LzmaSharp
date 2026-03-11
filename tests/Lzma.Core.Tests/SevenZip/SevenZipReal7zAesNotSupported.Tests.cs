using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipReal7zAesNotSupportedTests
{
  [Fact]
  public void DecodeToArray_Real7z_EncryptedData_HeaderVisible_NotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/hello_copy_7zaes_mhc_off.7z");

    // Header должен читаться, потому что он не зашифрован и не сжат.
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out int readConsumed));
    Assert.Equal(archive.Length, readConsumed);

    Assert.Equal(SevenZipNextHeaderKind.Header, reader.NextHeaderKind);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipHeader header = reader.Header!.Value;

    Assert.Equal(1u, header.FilesInfo.FileCount);
    Assert.NotNull(header.FilesInfo.Names);
    Assert.Single(header.FilesInfo.Names!);
    Assert.Equal("secret.bin", header.FilesInfo.Names![0].Replace('\\', '/'));

    // Данные зашифрованы => на этапе 1 это должно быть именно NotSupported.
    SevenZipArchiveDecodeResult r = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out _,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported, r);
    Assert.Equal(archive.Length, bytesConsumed);
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
