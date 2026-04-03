using System.Collections.Generic;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderNegativeNamesFlagsTests
{
  [Fact]
  public void TryRead_ДублирующийсяName_ЭтоInvalidData()
  {
    byte[] namePayload = BuildNamePayload("a");
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Name, namePayload),
      Property(SevenZipNid.Name, namePayload));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_NameСНечетнойДлинойUtf16_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Name,
      [
        0x00,
        0x61,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_NameБезЗавершающегоНуля_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Name,
      [
        0x00,
        0x61, 0x00,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ЛишнееИмяВName_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.Name, BuildNamePayload("a", "b")));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ZeroFilesСНепустымиИменами_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      0,
      Property(SevenZipNid.Name, BuildNamePayload("a")));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_EmptyFileБезEmptyStreamСНепустымPayload_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.EmptyFile,
      [
        0x80,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяEmptyStream_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.EmptyStream,
      [
        0x80,
      ]),
      Property(SevenZipNid.EmptyStream,
      [
        0x80,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяEmptyFile_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.EmptyStream,
      [
        0x80,
      ]),
      Property(SevenZipNid.EmptyFile,
      [
        0x80,
      ]),
      Property(SevenZipNid.EmptyFile,
      [
        0x80,
      ]));

    var r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.InvalidData, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_ДублирующийсяAnti_ЭтоInvalidData()
  {
    byte[] bytes = BuildFilesInfo(
      1,
      Property(SevenZipNid.EmptyStream,
      [
        0x80,
      ]),
      Property(SevenZipNid.Anti,
      [
        0x80,
      ]),
      Property(SevenZipNid.Anti,
      [
        0x80,
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

  private static byte[] BuildNamePayload(params string[] names)
  {
    var sb = new StringBuilder();

    foreach (string name in names)
      sb.Append(name).Append('\0');

    byte[] utf16 = Encoding.Unicode.GetBytes(sb.ToString());
    return
    [
      0x00,
      .. utf16,
    ];
  }
}
