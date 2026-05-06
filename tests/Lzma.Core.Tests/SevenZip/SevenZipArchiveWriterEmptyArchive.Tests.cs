using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterEmptyArchiveTests
{
  [Fact]
  public void BuildEmptyArchive_СоздаётАрхивКоторыйЧитаетсяDecoderPath()
  {
    SevenZipArchiveWriteResult writeResult = SevenZipArchiveWriter.BuildEmptyArchive(
        out byte[] archive);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, writeResult);
    Assert.NotEmpty(archive);

    SevenZipArchiveDecodeResult decodeResult = SevenZipArchiveDecoder.DecodeToEntries(
        archive,
        out SevenZipDecodedEntry[] entries,
        out int bytesConsumed);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decodeResult);
    Assert.Equal(archive.Length, bytesConsumed);
    Assert.Empty(entries);
  }
}
