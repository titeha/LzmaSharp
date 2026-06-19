using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterAutoTests
{
  private static byte[] CoderMethodId(byte[] archive)
  {
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    SevenZipFolder folder = Assert.Single(reader.Header!.Value.StreamsInfo.UnpackInfo!.Folders);
    return Assert.Single(folder.Coders).MethodId;
  }

  [Fact]
  public void Auto_ТекстовыеДанные_ВыбираетPpmd()
  {
    byte[] content = Encoding.UTF8.GetBytes(
        string.Concat(Enumerable.Repeat("Обычный текстовый документ со словами и пробелами. ", 100)));

    SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("doc.txt", content)],
        SevenZipWriterCompressionMethod.Auto,
        out byte[] archive);

    // PPMd method id = 03 04 01.
    Assert.Equal([0x03, 0x04, 0x01], CoderMethodId(archive));

    SevenZipArchiveDecodeResult decode = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive, out byte[] bytes, out _);
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decode);
    Assert.Equal(content, bytes);
  }

  [Fact]
  public void Auto_БинарныеДанные_ВыбираетLzma2()
  {
    // Псевдослучайные данные: много управляющих байт => не текст => LZMA2.
    var rnd = new Random(777);
    byte[] content = new byte[20000];
    rnd.NextBytes(content);

    SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("data.bin", content)],
        SevenZipWriterCompressionMethod.Auto,
        out byte[] archive);

    // LZMA2 method id = 0x21.
    Assert.Equal([0x21], CoderMethodId(archive));

    SevenZipArchiveDecodeResult decode = SevenZipArchiveDecoder.DecodeSingleFileToArray(
        archive, out byte[] bytes, out _);
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decode);
    Assert.Equal(content, bytes);
  }

  [Fact]
  public void Auto_НесколькоФайлов_RoundTrip()
  {
    byte[] first = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("text text text ", 200)));
    byte[] second = Encoding.UTF8.GetBytes("short");

    SevenZipArchiveWriter.BuildArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", first),
            new SevenZipArchiveWriterEntry("b.txt", second),
        ],
        SevenZipWriterCompressionMethod.Auto,
        out byte[] archive);

    SevenZipArchiveDecodeResult decode = SevenZipArchiveDecoder.DecodeToEntries(
        archive, out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, decode);
    Assert.Equal(2, entries.Length);
    Assert.Equal(first, entries[0].Bytes);
    Assert.Equal(second, entries[1].Bytes);
  }
}
