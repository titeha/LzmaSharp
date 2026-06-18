using System.Text;
using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipFilesInfoReaderUnknownAndLimitsTests
{
  [Fact]
  public void TryRead_НеизвестноеСвойство_Пропускается_ИНеЛомаетПарсингСледующихСвойств()
  {
    const byte unknownNid = 0xFE;
    byte[] namePayload = Encoding.Unicode.GetBytes("a\0");

    var bytes = new List<byte>
    {
      SevenZipNid.FilesInfo,
      0x01,
      unknownNid,
      0x03,
      0xDE, 0xAD, 0xBE,
      SevenZipNid.Name,
      (byte)(1 + namePayload.Length),
      0x00,
    };

    bytes.AddRange(namePayload);
    bytes.Add(SevenZipNid.End);

    SevenZipFilesInfoReadResult r = SevenZipFilesInfoReader.TryRead([.. bytes], out SevenZipFilesInfo files, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.Ok, r);
    Assert.Equal(bytes.Count, consumed);
    Assert.Equal((ulong)1, files.FileCount);

    Assert.NotNull(files.Names);
    Assert.Equal(["a"], files.Names!);
    Assert.Null(files.EmptyStreams);
    Assert.Null(files.CrcDefined);
    Assert.Null(files.Crc);
  }

  [Fact]
  public void TryRead_НеизвестноеСвойство_ЕслиЗаявленныйРазмерБольшеХвоста_ВозвращаетNeedMoreInput()
  {
    const byte unknownNid = 0xFE;

    byte[] bytes =
    [
      SevenZipNid.FilesInfo,
      0x01,
      unknownNid,
      0x03,
      0xAA,
    ];

    SevenZipFilesInfoReadResult r = SevenZipFilesInfoReader.TryRead(bytes, out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NeedMoreInput, r);
    Assert.Equal(0, consumed);
  }

  [Fact]
  public void TryRead_РазмерСвойстваБольшеIntMax_ВозвращаетNotSupported_ИНеПотребляетБайты()
  {
    const byte unknownNid = 0xFE;

    List<byte> bytes = [SevenZipNid.FilesInfo, 0x01, unknownNid];
    WriteU64(bytes, (ulong)int.MaxValue + 1UL);

    SevenZipFilesInfoReadResult r = SevenZipFilesInfoReader.TryRead([.. bytes], out _, out int consumed);

    Assert.Equal(SevenZipFilesInfoReadResult.NotSupported, r);
    Assert.Equal(0, consumed);
  }

  private static void WriteU64(List<byte> dst, ulong value)
  {
    Span<byte> tmp = stackalloc byte[10];
    SevenZipEncodedUInt64.WriteResult r = SevenZipEncodedUInt64.TryWrite(value, tmp, out int written);
    Assert.Equal(SevenZipEncodedUInt64.WriteResult.Ok, r);

    for (int i = 0; i < written; i++)
      dst.Add(tmp[i]);
  }
}
