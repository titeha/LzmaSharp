using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveDecoderEntriesOptionsTests
{
  [Fact]
  public void DecodeToEntries_НоваяПерегрузкаСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeToEntries(
            archive: archive,
            options: null!,
            entries: out _,
            bytesConsumed: out _));
  }

  [Fact]
  public void DecodeToEntries_НоваяПерегрузкаБезBytesConsumedСNullOptions_БросаетArgumentNullException()
  {
    byte[] archive = [];

    Assert.Throws<ArgumentNullException>(
        () => SevenZipArchiveDecoder.DecodeToEntries(
            archive: archive,
            options: null!,
            entries: out _));
  }

  [Fact]
  public void DecodeToEntries_НоваяПерегрузкаСDefaultOptions_ПовторяетСтароеПоведениеНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] oldEntries,
        out int oldBytesConsumed);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: SevenZipDecodeOptions.Default,
        entries: out SevenZipDecodedEntry[] newEntries,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldEntries.Length, newEntries.Length);
  }

  [Fact]
  public void DecodeToEntries_НоваяПерегрузкаСПаролем_НеМеняетРаннийParseResultНаБитомВходе()
  {
    byte[] archive = [0x37, 0x7A];

    SevenZipArchiveDecodeResult oldResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] oldEntries,
        out int oldBytesConsumed);

    using SevenZipPassword password = SevenZipPassword.FromString("secret");
    SevenZipDecodeOptions options = SevenZipDecodeOptions.WithPassword(password);

    SevenZipArchiveDecodeResult newResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive: archive,
        options: options,
        entries: out SevenZipDecodedEntry[] newEntries,
        bytesConsumed: out int newBytesConsumed);

    Assert.Equal(oldResult, newResult);
    Assert.Equal(oldBytesConsumed, newBytesConsumed);
    Assert.Equal(oldEntries.Length, newEntries.Length);
  }
}
