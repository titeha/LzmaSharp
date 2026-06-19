using System.IO.Compression;
using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

public sealed class ZipWriterTests
{
  private static ZipWriterEntry[] SampleEntries() =>
  [
    new ZipWriterEntry("hello.txt", Encoding.UTF8.GetBytes("Hello, ZIP writer!")),
    new ZipWriterEntry("dir", [], IsDirectory: true),
    new ZipWriterEntry("dir/repeated.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeat ", 3000)))),
    new ZipWriterEntry("dir/empty.txt", []),
    new ZipWriterEntry("пример.txt", Encoding.UTF8.GetBytes("UTF-8 имя и содержимое")),
  ];

  [Fact]
  public void Build_RoundTripЧерезНашReader()
  {
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(SampleEntries(), out byte[] zip));

    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(zip, out ZipEntry[] entries));

    foreach (ZipWriterEntry src in SampleEntries())
    {
      if (src.IsDirectory)
      {
        Assert.Contains(entries, e => e.IsDirectory && e.Name == src.Name + "/");
        continue;
      }

      ZipEntry got = Assert.Single(entries, e => e.Name == src.Name && !e.IsDirectory);
      Assert.Equal(src.Content, got.Bytes);
    }
  }

  [Fact]
  public void Build_ЧитаетсяBclZipArchive()
  {
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(SampleEntries(), out byte[] zip));

    using var ms = new MemoryStream(zip);
    using var bcl = new ZipArchive(ms, ZipArchiveMode.Read);

    foreach (ZipWriterEntry src in SampleEntries())
    {
      if (src.IsDirectory)
        continue;

      ZipArchiveEntry? entry = bcl.GetEntry(src.Name);
      Assert.NotNull(entry);

      using Stream s = entry!.Open();
      using var outMs = new MemoryStream();
      s.CopyTo(outMs);
      Assert.Equal(src.Content, outMs.ToArray());
    }
  }

  [Fact]
  public void Build_СжимаетПовторяющиесяДанные()
  {
    var entries = new[]
    {
      new ZipWriterEntry("big.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compress me ", 5000)))),
    };

    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(entries, out byte[] zip));

    // Архив заметно меньше исходного содержимого.
    Assert.True(zip.Length < entries[0].Content.Length / 2);
  }

  [Fact]
  public void Build_ПустойНабор_ВозвращаетВалидныйПустойАрхив()
  {
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build([], out byte[] zip));

    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(zip, out ZipEntry[] entries));
    Assert.Empty(entries);

    using var ms = new MemoryStream(zip);
    using var bcl = new ZipArchive(ms, ZipArchiveMode.Read);
    Assert.Empty(bcl.Entries);
  }

  [Fact]
  public void Build_NullСодержимое_ВозвращаетInvalidData()
  {
    var entries = new[] { new ZipWriterEntry("x", null!) };
    Assert.Equal(ZipWriteResult.InvalidData, ZipWriter.Build(entries, out _));
  }
}
