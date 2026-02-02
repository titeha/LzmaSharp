using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipUnpackInfoReaderTests
{
  [Fact]
  public void TryRead_MinimalOneFolderOneCoder_ReturnsOk()
  {
    // UnpackInfo ::= kUnpackInfo
    //               kFolder NumFolders=1 External=0
    //                 Folder: NumCoders=1
    //                   Coder: mainByte(idSize=1), id=0x21 (LZMA2)
    //               kCodersUnpackSize [5]
    //               kEnd
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,

      0x01,
      0x01,
      0x21,

      SevenZipNid.CodersUnpackSize,
      0x05,
      SevenZipNid.End,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.Ok, res);
    Assert.Equal(data.Length, consumed);

    Assert.NotNull(unpackInfo);
    Assert.NotNull(unpackInfo.Folders);
    Assert.Single(unpackInfo.Folders);

    SevenZipFolder folder = unpackInfo.Folders[0];
    Assert.Equal((ulong)1, folder.NumInStreams);
    Assert.Equal((ulong)1, folder.NumOutStreams);

    Assert.Single(folder.Coders);
    SevenZipCoderInfo coder = folder.Coders[0];
    Assert.True(coder.MethodId.SequenceEqual(new byte[] { 0x21 }));
    Assert.Empty(coder.Properties);
    Assert.Equal((ulong)1, coder.NumInStreams);
    Assert.Equal((ulong)1, coder.NumOutStreams);

    Assert.NotNull(unpackInfo.FolderUnpackSizes);
    Assert.Single(unpackInfo.FolderUnpackSizes);
    Assert.Single(unpackInfo.FolderUnpackSizes[0]);
    Assert.Equal((ulong)5, unpackInfo.FolderUnpackSizes[0][0]);
  }

  [Fact]
  public void TryRead_Truncated_ReturnsNeedMoreInput_AndConsumesNothing()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,
      0x01,
      0x01,
      0x21,
      SevenZipNid.CodersUnpackSize,
      0x05,
      // нет kEnd
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_ExternalFolders_NotSupported()
  {
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x01, // External=1
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.NotSupported, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }

  [Fact]
  public void TryRead_Crc_NotSupported()
  {
    // То же, что минимальный кейс, но после unpack size идёт kCRC.
    byte[] data =
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01,
      0x00,

      0x01,
      0x01,
      0x21,

      SevenZipNid.CodersUnpackSize,
      0x05,
      SevenZipNid.Crc,
      0x00,
    ];

    var res = SevenZipUnpackInfoReader.TryRead(data, out SevenZipUnpackInfo unpackInfo, out int consumed);
    Assert.Equal(SevenZipUnpackInfoReadResult.NotSupported, res);
    Assert.Equal(0, consumed);
    Assert.Null(unpackInfo);
  }
}
