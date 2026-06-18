using System.Collections.Generic;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderNegativeMetadataTests
{
  [Fact]
  public void TryRead_ДублирующийсяCrc_ЭтоInvalidData()
  {
    byte[] crcPayload =
    [
      0x01,
      0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc, crcPayload),
      Property(SevenZipNid.Crc, crcPayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_CrcСНекорректнымAllAreDefined_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc,
      [
        0x02,
        0x44, 0x33, 0x22, 0x11,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_CrcСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Crc,
      [
        0x01,
        0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяMTime_ЭтоInvalidData()
  {
    byte[] mTimePayload =
    [
      0x01,
      0x00,
      0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime, mTimePayload),
      Property(SevenZipNid.MTime, mTimePayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeExternalData_ЭтоNotSupported()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x01,
        0x01,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeСНекорректнымAllAreDefined_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x02,
        0x00,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_MTimeСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.MTime,
      [
        0x01,
        0x00,
        0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_WinAttribExternalData_ЭтоNotSupported()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib,
      [
        0x01,
        0x01,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяWinAttrib_ЭтоInvalidData()
  {
    byte[] winAttribPayload =
    [
      0x01,
      0x00,
      0x44, 0x33, 0x22, 0x11,
    ];

    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib, winAttribPayload),
      Property(SevenZipNid.WinAttrib, winAttribPayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_WinAttribСЛишнимХвостом_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.WinAttrib,
      [
        0x01,
        0x00,
        0x44, 0x33, 0x22, 0x11,
        0x99,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  private static byte[] BuildFilesInfo(byte fileCount, params byte[][] properties)
  {
    var bytes = new List<byte>
    {
      SevenZipNid.FilesInfo,
      fileCount,
    };

    foreach (byte[] property in properties)
      bytes.AddRange(property);

    bytes.Add(SevenZipNid.End);
    return [.. bytes];
  }

  private static byte[] Property(byte nid, params byte[] payload)
  {
    var bytes = new List<byte>
    {
      nid,
      checked((byte)payload.Length),
    };

    bytes.AddRange(payload);
    return [.. bytes];
  }
}
