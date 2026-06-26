using Lzma.Core.SevenZip;
using Lzma.Ui.Models;

namespace Lzma.Ui.Tests;

public sealed class CompressionMethodOptionTests
{
  [Theory]
  [InlineData(SevenZipWriterCompressionMethod.Lzma2)]
  [InlineData(SevenZipWriterCompressionMethod.Ppmd)]
  [InlineData(SevenZipWriterCompressionMethod.Auto)]
  [InlineData(SevenZipWriterCompressionMethod.Copy)]
  public void ForMethod_СохраняетМетодИДаётНепустоеИмя(SevenZipWriterCompressionMethod method)
  {
    CompressionMethodOption option = CompressionMethodOption.ForMethod(method);

    Assert.Equal(method, option.Method);
    Assert.False(string.IsNullOrWhiteSpace(option.DisplayName));
  }
}
