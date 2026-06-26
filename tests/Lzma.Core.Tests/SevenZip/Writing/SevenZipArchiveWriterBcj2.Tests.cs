using System.Linq;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterBcj2Tests
{
  // Полу-реалистичный x86-поток: случайный фон + регулярные E8/E9 с короткими смещениями.
  private static byte[] MakeX86Like(int length, uint seed)
  {
    byte[] data = new byte[length];
    uint x = seed;

    for (int i = 0; i < length; i++)
    {
      x ^= x << 13;
      x ^= x >> 17;
      x ^= x << 5;
      data[i] = (byte)x;
    }

    for (int i = 16; i + 8 < length; i += 29)
    {
      data[i] = (i % 2 == 0) ? (byte)0xE8 : (byte)0xE9;
      int rel = (i * 5) % 512;
      data[i + 1] = (byte)rel;
      data[i + 2] = (byte)(rel >> 8);
      data[i + 3] = 0;
      data[i + 4] = 0;
    }

    return data;
  }

  [Fact]
  public void BuildBcj2_ОдинФайл_RoundTrip()
  {
    byte[] content = MakeX86Like(4096, 0xABCDEF01);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [new SevenZipArchiveWriterEntry("app.bin", content)], out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.Equal("app.bin", entry.Name);
    Assert.Equal(content, entry.Bytes);
  }

  [Fact]
  public void BuildBcj2_НесколькоФайлов_RoundTrip()
  {
    byte[] a = MakeX86Like(2000, 0x11111111);
    byte[] b = MakeX86Like(5000, 0x22222222);
    byte[] c = MakeX86Like(1, 0x33333333);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [
            new SevenZipArchiveWriterEntry("a.bin", a),
            new SevenZipArchiveWriterEntry("dir/b.bin", b),
            new SevenZipArchiveWriterEntry("c.bin", c),
        ],
        out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    Assert.Equal(3, entries.Length);
    Assert.Equal(a, entries.Single(e => e.Name.Replace('\\', '/') == "a.bin").Bytes);
    Assert.Equal(b, entries.Single(e => e.Name.Replace('\\', '/') == "dir/b.bin").Bytes);
    Assert.Equal(c, entries.Single(e => e.Name.Replace('\\', '/') == "c.bin").Bytes);
  }

  [Fact]
  public void BuildBcj2_СмешанныйСПустыми_RoundTrip()
  {
    byte[] content = MakeX86Like(3000, 0x44444444);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [
            new SevenZipArchiveWriterEntry("readme", []),                 // пустой файл
            new SevenZipArchiveWriterEntry("bin/app.exe", content),       // непустой → BCJ2
            new SevenZipArchiveWriterEntry("emptydir", [], IsDirectory: true),
        ],
        out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    Assert.Equal(3, entries.Length);
    Assert.Equal(content, entries.Single(e => e.Name.Replace('\\', '/') == "bin/app.exe").Bytes);
  }
}
