using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderExtractOptionsTests
{
  [Fact]
  public void ExtractToDirectory_НоваяПерегрузкаСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];
    string root = CreateTempRoot();

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.ExtractToDirectory(
            archive: archive,
            options: null!,
            destinationDirectory: root,
            overwrite: false,
            bytesConsumed: out _));
  }

  [Fact]
  public void ExtractToDirectory_НоваяПерегрузкаБезBytesConsumedСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];
    string root = CreateTempRoot();

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.ExtractToDirectory(
            archive: archive,
            options: null!,
            destinationDirectory: root,
            overwrite: false));
  }

  [Fact]
  public void ExtractToDirectory_НоваяПерегрузкаСDefaultOptions_ПовторяетСтароеПоведениеНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];
    string rootOld = CreateTempRoot();
    string rootNew = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          destinationDirectory: rootOld,
          overwrite: false,
          bytesConsumed: out int oldBytesConsumed);

      SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: SevenZipDecodeOptions.Default,
          destinationDirectory: rootNew,
          overwrite: false,
          bytesConsumed: out int newBytesConsumed);

      Assert.Equal(oldResult, newResult);
      Assert.Equal(oldBytesConsumed, newBytesConsumed);
    }
    finally
    {
      TryDeleteTree(rootOld);
      TryDeleteTree(rootNew);
    }
  }

  [Fact]
  public void ExtractToDirectory_НоваяПерегрузкаСПаролем_НеМеняетРаннийParseResultНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];
    string rootOld = CreateTempRoot();
    string rootNew = CreateTempRoot();

    try
    {
      SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          destinationDirectory: rootOld,
          overwrite: false,
          bytesConsumed: out int oldBytesConsumed);

      using SevenZipPassword password = SevenZipPassword.FromString("secret");
      SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

      SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.ExtractToDirectory(
          archive: archive,
          options: options,
          destinationDirectory: rootNew,
          overwrite: false,
          bytesConsumed: out int newBytesConsumed);

      Assert.Equal(oldResult, newResult);
      Assert.Equal(oldBytesConsumed, newBytesConsumed);
    }
    finally
    {
      TryDeleteTree(rootOld);
      TryDeleteTree(rootNew);
    }
  }

  [Fact]
  public void ExtractToDirectory_НоваяПерегрузкаСNullDestination_ВозвращаетInvalidData()
  {
    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
        archive: [],
        options: options,
        destinationDirectory: null!,
        overwrite: false,
        bytesConsumed: out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
    Assert.Equal(0, bytesConsumed);
  }

  private static string CreateTempRoot()
  {
    return Path.Combine(
        Path.GetTempPath(),
        "LzmaSharpTests",
        nameof(SevenZipArchiveDecoderExtractOptionsTests),
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
