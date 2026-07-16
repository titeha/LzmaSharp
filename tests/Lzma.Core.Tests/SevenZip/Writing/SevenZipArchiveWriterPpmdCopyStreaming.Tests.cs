using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового пофайлового создания PPMd/Copy-архивов (без загрузки всего набора в память):
/// round-trip через наш декодер.
/// </summary>
public sealed class SevenZipArchiveWriterPpmdCopyStreamingTests
{
  private static List<SevenZipStreamingEntry> Files()
  {
    var list = new List<SevenZipStreamingEntry>
    {
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
      new("a.txt", 6, () => new MemoryStream(Encoding.UTF8.GetBytes("привет"))),  // 6 байт utf8? нет — кириллица 2б/симв
    };
    byte[] a = Encoding.UTF8.GetBytes("привет");
    list[1] = new("a.txt", a.LongLength, () => new MemoryStream(a));
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("PPMd поток 0123456789 ", 6000)));
    list.Add(new("dir/big.txt", big.LongLength, () => new MemoryStream(big)));
    list.Add(new("empty.txt", 0, () => new MemoryStream([])));
    return list;
  }

  private static void RoundTrip(byte[] archive, List<SevenZipStreamingEntry> entries)
  {
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));
    Assert.Equal(entries.Count, decoded.Length);
    for (int i = 0; i < entries.Count; i++)
    {
      Assert.Equal(entries[i].Name, decoded[i].Name);
      using var s = entries[i].OpenRead();
      using var buf = new MemoryStream();
      s.CopyTo(buf);
      Assert.Equal(buf.ToArray(), decoded[i].Bytes);
    }
  }

  [Fact]
  public void PPMd_Потоково_RoundTrip()
  {
    var entries = Files();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildPpmdArchiveToStream(entries, ms));
    RoundTrip(ms.ToArray(), entries);
  }

  [Fact]
  public void Copy_Потоково_RoundTrip()
  {
    var entries = Files();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildCopyArchiveToStream(entries, ms));
    RoundTrip(ms.ToArray(), entries);
  }
}
