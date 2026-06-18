using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderEmptyFileAntiTests
{
  [Fact]
  public void TryRead_EmptyStream_EmptyFile_Anti_РазворачиваютсяНаВсеФайлы()
  {
    // NumFiles = 3
    // EmptyStream: [true, false, true] => 0b1010_0000 = 0xA0
    // EmptyStreams list = (file0, file2) => count = 2
    // EmptyFile:  [false, true] => 0b0100_0000 = 0x40  (только для пустых потоков)
    // Anti:       [true, false] => 0b1000_0000 = 0x80
    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x03,

      SevenZipNid.EmptyStream,
      0x01,
      0xA0,

      SevenZipNid.EmptyFile,
      0x01,
      0x40,

      SevenZipNid.Anti,
      0x01,
      0x80,

      SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);

    Assert.NotNull(files.EmptyStreams);
    Assert.Equal([true, false, true], files.EmptyStreams!);

    Assert.NotNull(files.EmptyFiles);
    Assert.Equal([false, false, true], files.EmptyFiles!);

    Assert.NotNull(files.Anti);
    Assert.Equal([true, false, false], files.Anti!);
  }

  [Fact]
  public void TryRead_EmptyFileДоEmptyStream_ПорядокНеВажен()
  {
    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x03,

      SevenZipNid.EmptyFile,
      0x01,
      0x40,

      SevenZipNid.Anti,
      0x01,
      0x80,

      SevenZipNid.EmptyStream,
      0x01,
      0xA0,

      SevenZipNid.End,
    ];

    var r = SevenZipFilesInfoReader.TryRead(bytes, out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Length, consumed);

    Assert.Equal([true, false, true], files.EmptyStreams!);
    Assert.Equal([false, false, true], files.EmptyFiles!);
    Assert.Equal([true, false, false], files.Anti!);
  }
}
