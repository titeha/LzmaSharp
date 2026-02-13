using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public class SevenZipFilesInfoReaderEmptyStreamTests
{
  [Fact]
  public void TryRead_EmptyStreamVector_ParsesCorrectly()
  {
    // FilesInfo:
    // - fileCount = 3
    // - kEmptyStream payload:
    //   allAreDefined = 0
    //   bits: [false, true, false] => 0x40 (0b0100_0000), т.к. биты идут 0x80,0x40,0x20...
    byte[] src =
    [
        SevenZipNid.FilesInfo,
            3,
            SevenZipNid.EmptyStream,
            2,
            0,
            0x40,
            SevenZipNid.End
    ];

    SevenZipFilesInfoReadResult r = SevenZipFilesInfoReader.TryRead(src, out SevenZipFilesInfo filesInfo, out int bytesConsumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(src.Length, bytesConsumed);

    Assert.NotNull(filesInfo.EmptyStreams);
    Assert.Equal(new[] { false, true, false }, filesInfo.EmptyStreams!);
  }
}
