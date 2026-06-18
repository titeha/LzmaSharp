using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipStreamsInfoReaderTests
{
  [Fact]
  public void TryRead_NeedMoreInput_ЕслиБуферПустой()
  {
    var res = SevenZipStreamsInfoReader.TryRead(
      [],
      out var streamsInfo,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.NeedMoreInput, res);
    Assert.Equal(0, bytesConsumed);

    _ = streamsInfo;
  }

  [Fact]
  public void TryRead_Ok_ЕслиЕстьPackInfoИUnpackInfo()
  {
    byte[] data = Concat(
      CreateMinimalPackInfo(),
      CreateMinimalUnpackInfo(7),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out var streamsInfo,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);

    Assert.NotNull(streamsInfo.PackInfo);
    Assert.NotNull(streamsInfo.UnpackInfo);
    Assert.Null(streamsInfo.SubStreamsInfo);

    Assert.Equal(7UL, streamsInfo.UnpackInfo!.FolderUnpackSizes[0][0]);
  }

  [Fact]
  public void TryRead_Ok_ЕслиЕстьSubStreamsInfo_СПустымТелом()
  {
    byte[] data = Concat(
      CreateMinimalPackInfo(),
      CreateMinimalUnpackInfo(9),
      CreateMinimalSubStreamsInfo(),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out var streamsInfo,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.Ok, res);
    Assert.Equal(data.Length, bytesConsumed);

    Assert.NotNull(streamsInfo.SubStreamsInfo);
    Assert.Equal(1UL, streamsInfo.SubStreamsInfo!.NumUnpackStreamsPerFolder[0]);
    Assert.Equal(9UL, streamsInfo.SubStreamsInfo.UnpackSizesPerFolder[0][0]);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиUnpackInfoИдетДоPackInfo()
  {
    byte[] data = Concat(
      CreateMinimalUnpackInfo(5),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиSubStreamsInfoИдетДоUnpackInfo()
  {
    byte[] data = Concat(
      CreateMinimalPackInfo(),
      CreateMinimalSubStreamsInfo(),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиPackInfoПовторяется()
  {
    byte[] data = Concat(
      CreateMinimalPackInfo(),
      CreateMinimalPackInfo(),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  [Fact]
  public void TryRead_InvalidData_ЕслиUnpackInfoПовторяется()
  {
    byte[] data = Concat(
      CreateMinimalPackInfo(),
      CreateMinimalUnpackInfo(5),
      CreateMinimalUnpackInfo(6),
      new byte[] { SevenZipNid.End });

    var res = SevenZipStreamsInfoReader.TryRead(
      data,
      out _,
      out int bytesConsumed);

    Assert.Equal(SevenZipStreamsInfoReadResult.InvalidData, res);
    Assert.Equal(0, bytesConsumed);
  }

  private static byte[] CreateMinimalPackInfo()
  {
    return
    [
      SevenZipNid.PackInfo,
      0x00, // packPos = 0
      0x01, // numPackStreams = 1
      SevenZipNid.Size,
      0x03, // packSize = 3
      SevenZipNid.End,
    ];
  }

  private static byte[] CreateMinimalUnpackInfo(byte unpackSize)
  {
    return
    [
      SevenZipNid.UnpackInfo,
      SevenZipNid.Folder,
      0x01, // numFolders = 1
      0x00, // external = 0

      0x01, // numCoders = 1
      0x01, // mainByte: MethodIdSize = 1
      0x21, // произвольный MethodId

      SevenZipNid.CodersUnpackSize,
      unpackSize,
      SevenZipNid.End,
    ];
  }

  private static byte[] CreateMinimalSubStreamsInfo()
  {
    return
    [
      SevenZipNid.SubStreamsInfo,
      SevenZipNid.End,
    ];
  }

  private static byte[] Concat(params byte[][] parts)
  {
    int length = 0;
    for (int i = 0; i < parts.Length; i++)
      length += parts[i].Length;

    byte[] result = new byte[length];
    int offset = 0;
    for (int i = 0; i < parts.Length; i++)
    {
      Buffer.BlockCopy(parts[i], 0, result, offset, parts[i].Length);
      offset += parts[i].Length;
    }

    return result;
  }
}
