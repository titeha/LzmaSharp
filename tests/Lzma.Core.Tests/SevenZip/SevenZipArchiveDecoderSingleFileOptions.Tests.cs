using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderSingleFileOptionsTests
{
  [Fact]
  public void DecodeSingleFileToArray_НоваяПерегрузкаСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeSingleFileToArray(
            archiveBytes: archive,
            options: null!,
            fileBytes: out _,
            fileName: out _,
            bytesConsumed: out _));
  }

  [Fact]
  public void DecodeSingleFileToArray_НоваяПерегрузкаБезBytesConsumedСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeSingleFileToArray(
            archiveBytes: archive,
            options: null!,
            fileBytes: out _,
            fileName: out _));
  }

  [Fact]
  public void DecodeSingleFileToArray_НоваяПерегрузкаСDefaultOptions_ПовторяетСтароеПоведениеНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] oldBytes,
        out string oldName,
        out int oldBytesConsumed);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: SevenZipDecodeOptions.Default,
        fileBytes: out byte[] newBytes,
        fileName: out string newName,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldBytes, newBytes);
    Assert.Equal(oldName, newName);
  }

  [Fact]
  public void DecodeSingleFileToArray_НоваяПерегрузкаСПаролем_НеМеняетРаннийParseResultНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive,
        out byte[] oldBytes,
        out string oldName,
        out int oldBytesConsumed);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archiveBytes: archive,
        options: options,
        fileBytes: out byte[] newBytes,
        fileName: out string newName,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldBytes, newBytes);
    Assert.Equal(oldName, newName);
  }
}
