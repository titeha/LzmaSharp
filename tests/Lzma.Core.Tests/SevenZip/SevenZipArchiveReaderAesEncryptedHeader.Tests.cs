using System.Runtime.CompilerServices;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveReaderAesEncryptedHeaderTests
{
  private const string Password = "LzmaSharp-AES-Stage15";

  [Fact]
  public void Read_РеальныйAesАрхивСЗашифрованнымHeader_СПаролем_ВозвращаетOkИHeader()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    var reader = new SevenZipArchiveReader();

    using SevenZipPassword password = SevenZipPassword.FromString(Password);

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.Ok, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Equal(SevenZipNextHeaderKind.EncodedHeader, reader.NextHeaderKind);

    Assert.True(reader.Header.HasValue);
    Assert.False(reader.DecodedHeaderBytes.IsEmpty);

    SevenZipHeader header = reader.Header.Value;
    Assert.NotNull(header.FilesInfo.Names);
    Assert.Contains("aes-mhe-on-real.txt", header.FilesInfo.Names!);
  }

  [Fact]
  public void Read_РеальныйAesАрхивСЗашифрованнымHeader_БезПароля_ВозвращаетNotSupported()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    var reader = new SevenZipArchiveReader();

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.Default,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.NotSupported, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);
  }

  [Fact]
  public void Read_РеальныйAesАрхивСЗашифрованнымHeader_СНевернымПаролем_ВозвращаетInvalidData()
  {
    byte[] archive = ReadTestDataBytes("TestData/Real/aes_lzma2_singlefile_pwd_mhe_on.7z");

    var reader = new SevenZipArchiveReader();

    using SevenZipPassword password = SevenZipPassword.FromString("wrong-password");

    SevenZipArchiveReadResult result = reader.Read(
        input: archive,
        options: SevenZipDecodeOptions.WithPassword(password),
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveReadResult.InvalidData, result);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.False(reader.Header.HasValue);
    Assert.True(reader.DecodedHeaderBytes.IsEmpty);
  }

  [Fact]
  public void Read_СNullOptions_БросаетArgumentNullException()
  {
    var reader = new SevenZipArchiveReader();

    Assert.Throws<ArgumentNullException>(
        () => reader.Read(
            input: [],
            options: null!,
            bytesConsumed: out _));
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
