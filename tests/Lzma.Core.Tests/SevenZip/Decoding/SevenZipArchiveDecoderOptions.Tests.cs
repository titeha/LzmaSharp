using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderOptionsTests
{
  [Fact]
  public void DecodeToArray_НоваяПерегрузкаСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeToArray(
            archive: archive,
            options: null!,
            files: out _,
            bytesConsumed: out _));
  }

  [Fact]
  public void DecodeToArray_НоваяПерегрузкаСDefaultOptions_ПовторяетСтароеПоведениеНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] oldFiles,
        out int oldBytesConsumed);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        files: out SevenZipDecodedFile[] newFiles,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldFiles.Length, newFiles.Length);
  }

  [Fact]
  public void DecodeToArray_НоваяПерегрузкаСПаролем_НеМеняетРаннийParseResultНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeToArray(
        archive,
        out SevenZipDecodedFile[] oldFiles,
        out int oldBytesConsumed);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeToArray(
        archive: archive,
        options: options,
        files: out SevenZipDecodedFile[] newFiles,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldFiles.Length, newFiles.Length);
  }
}
