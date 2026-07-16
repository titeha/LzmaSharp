using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты листинга содержимого архива из Stream БЕЗ распаковки (ListEntriesFromStream): имена,
/// каталоги и размеры для обзора больших архивов.
/// </summary>
public sealed class SevenZipListEntriesFromStreamTests
{
  [Fact]
  public void ЛистингИзStream_ИменаКаталогиРазмеры()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("листинг 0123456789 ", 5000)));

    var entries = new List<SevenZipStreamingEntry>
    {
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
      new("a.txt", a.LongLength, () => new MemoryStream(a)),
      new("empty.txt", 0, () => new MemoryStream([])),
      new("dir/big.bin", big.LongLength, () => new MemoryStream(big)),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, ms, 1 << 20));

    ms.Position = 0;
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.ListEntriesFromStream(ms, out SevenZipListedEntry[] listed));

    Assert.Equal(4, listed.Length);
    Assert.Equal("dir", listed[0].Name);
    Assert.True(listed[0].IsDirectory);
    Assert.Equal("a.txt", listed[1].Name);
    Assert.False(listed[1].IsDirectory);
    Assert.Equal(a.LongLength, listed[1].Size);
    Assert.Equal("empty.txt", listed[2].Name);
    Assert.Equal(0, listed[2].Size);
    Assert.Equal("dir/big.bin", listed[3].Name);
    Assert.Equal(big.LongLength, listed[3].Size);
  }
}
