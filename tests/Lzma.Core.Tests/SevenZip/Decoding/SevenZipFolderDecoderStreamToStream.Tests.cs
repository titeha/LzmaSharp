using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового декода folder-а из архива-Stream (SevenZipFolderDecoder.DecodeFolderStreamToStream):
/// packed берётся по смещению из архива без загрузки в память → извлечение архивов больше 2 ГиБ.
/// </summary>
public sealed class SevenZipFolderDecoderStreamToStreamTests
{
  [Fact]
  public void ДекодируетКаждыйFolderИзStream_ПоСмещению()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет-привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Folder из Stream 0123456789 ", 6000)));
    byte[] c = Encoding.UTF8.GetBytes("конец");
    byte[][] files = [a, big, c];

    var entries = new List<SevenZipStreamingEntry>
    {
      new("a.txt", a.LongLength, () => new MemoryStream(a)),
      new("big.bin", big.LongLength, () => new MemoryStream(big)),
      new("c.txt", c.LongLength, () => new MemoryStream(c)),
    };

    using var archiveMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, archiveMs, 1 << 20));

    archiveMs.Position = 0;

    SevenZipArchiveDecodeResult hr = SevenZipArchiveStreamReader.ReadHeader(
        archiveMs, out SevenZipHeader header, out long packedBase);
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, hr);

    // Каждый файл — свой folder (наш writer: 1 файл = 1 folder). Декодируем по очереди.
    for (int folderIndex = 0; folderIndex < files.Length; folderIndex++)
    {
      using var outMs = new MemoryStream();
      SevenZipFolderDecodeResult r = SevenZipFolderDecoder.DecodeFolderStreamToStream(
          header.StreamsInfo!, archiveMs, packedBase, folderIndex, SevenZipDecodeOptions.Default,
          outMs, out long written);

      Assert.Equal(SevenZipFolderDecodeResult.Ok, r);
      Assert.Equal(files[folderIndex].LongLength, written);
      Assert.Equal(files[folderIndex], outMs.ToArray());
    }
  }

  [Fact]
  public void СовпадаетСоSpanДекодом()
  {
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("сверка span vs stream ", 5000)));
    var entries = new List<SevenZipStreamingEntry>
    {
      new("f.bin", data.LongLength, () => new MemoryStream(data)),
    };

    using var archiveMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, archiveMs, 1 << 20));

    byte[] archive = archiveMs.ToArray();

    // span-путь (эталон).
    var reader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, reader.Read(archive, out _));
    SevenZipFolderDecodeResult spanRes = SevenZipFolderDecoder.DecodeFolderToArray(
        reader.Header!.Value.StreamsInfo!, reader.PackedStreams.Span, 0, out byte[] fromSpan);
    Assert.Equal(SevenZipFolderDecodeResult.Ok, spanRes);

    // stream-путь.
    archiveMs.Position = 0;
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveStreamReader.ReadHeader(archiveMs, out SevenZipHeader header, out long packedBase));
    using var outMs = new MemoryStream();
    Assert.Equal(SevenZipFolderDecodeResult.Ok, SevenZipFolderDecoder.DecodeFolderStreamToStream(
        header.StreamsInfo!, archiveMs, packedBase, 0, SevenZipDecodeOptions.Default, outMs, out _));

    Assert.Equal(fromSpan, outMs.ToArray());
    Assert.Equal(data, outMs.ToArray());
  }
}
