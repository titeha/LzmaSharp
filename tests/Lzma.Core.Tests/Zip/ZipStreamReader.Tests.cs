using System.IO.Compression;
using System.Text;

using Lzma.Core.Checksums;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковое чтение центрального каталога ZIP из <see cref="Stream"/> (без загрузки данных).
/// Сверяется с одноразовым span-<see cref="ZipReader"/> и с эталонным BCL <see cref="ZipArchive"/>.
/// </summary>
public sealed class ZipStreamReaderTests
{
  [Fact]
  public void Каталог_СовпадаетСоSpanReader_ФайлыИПапки()
  {
    byte[] a = Encoding.UTF8.GetBytes("first file contents, compressible compressible compressible");
    byte[] b = Encoding.UTF8.GetBytes(new string('x', 5000)); // хорошо жмётся → Deflate
    byte[] empty = [];

    ZipWriteResult wr = ZipWriter.Build(
    [
        new ZipWriterEntry("dir/", [], IsDirectory: true),
        new ZipWriterEntry("dir/a.txt", a),
        new ZipWriterEntry("b.bin", b),
        new ZipWriterEntry("empty.txt", empty),
    ], out byte[] archive);
    Assert.Equal(ZipWriteResult.Ok, wr);

    // span-reader (эталон: даёт распакованное содержимое)
    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(archive, out ZipEntry[] spanEntries));

    // потоковый reader (только метаданные)
    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] streamEntries));

    Assert.Equal(spanEntries.Length, streamEntries.Length);

    for (int i = 0; i < spanEntries.Length; i++)
    {
      Assert.Equal(spanEntries[i].Name, streamEntries[i].Name);
      Assert.Equal(spanEntries[i].IsDirectory, streamEntries[i].IsDirectory);

      if (!spanEntries[i].IsDirectory)
      {
        Assert.Equal(spanEntries[i].Bytes.Length, streamEntries[i].UncompressedSize);
        Assert.Equal(Crc32.Compute(spanEntries[i].Bytes), streamEntries[i].Crc);
      }
    }
  }

  [Fact]
  public void Каталог_ЧитаетАрхивBcl()
  {
    // Независимый эталон: ZIP, собранный BCL.
    byte[] archive = BuildBclZip(
        ("readme.txt", Encoding.UTF8.GetBytes("hello from bcl zip")),
        ("data/payload.bin", Encoding.UTF8.GetBytes(new string('q', 8000))));

    using var ms = new MemoryStream(archive, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    Assert.Equal(2, entries.Length);
    Assert.Contains(entries, e => e.Name == "readme.txt" && !e.IsDirectory && e.UncompressedSize == 18);
    Assert.Contains(entries, e => e.Name == "data/payload.bin" && e.UncompressedSize == 8000);
  }

  [Fact]
  public void НеSeekableПоток_InvalidData()
  {
    ZipWriter.Build([new ZipWriterEntry("a.txt", Encoding.UTF8.GetBytes("x"))], out byte[] archive);

    using var forward = new ForwardOnlyStream(archive);
    Assert.Equal(ZipReadResult.InvalidData, ZipStreamReader.ReadCentralDirectory(forward, out _));
  }

  [Fact]
  public void Мусор_InvalidData()
  {
    byte[] garbage = new byte[500];
    for (int i = 0; i < garbage.Length; i++)
      garbage[i] = (byte)(i * 7);

    using var ms = new MemoryStream(garbage, writable: false);
    Assert.Equal(ZipReadResult.InvalidData, ZipStreamReader.ReadCentralDirectory(ms, out _));
  }

  [Fact]
  public void ZIP64_МногоЗаписей_ЧитаетВесьКаталог()
  {
    // BCL пишет ZIP64, когда записей > 65535 (16-битный счётчик EOCD переполняется).
    const int count = 70_000;

    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      for (int i = 0; i < count; i++)
        zip.CreateEntry($"f{i}.txt"); // пустые записи — архив компактный
    }

    ms.Position = 0;
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    Assert.Equal(count, entries.Length);
    Assert.Equal("f0.txt", entries[0].Name);
    Assert.Equal($"f{count - 1}.txt", entries[count - 1].Name);
  }

  [Fact]
  public void ZIP64_ПоэлементныйExtra_РазмерыИСмещениеИзExtra()
  {
    byte[] content = Encoding.UTF8.GetBytes("zip64 per-entry extra field payload");
    uint crc = Crc32.Compute(content);
    byte[] name = Encoding.ASCII.GetBytes("z64.txt");

    var buf = new List<byte>();

    long localOffset = buf.Count; // 0
    WriteU32(buf, 0x04034b50);                 // local file header
    WriteU16(buf, 45);                         // version needed (ZIP64)
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method = store
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, (uint)content.Length);       // comp size (в локальном — реальные)
    WriteU32(buf, (uint)content.Length);       // uncomp size
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 0);                          // extra len
    buf.AddRange(name);
    buf.AddRange(content);

    long cdStart = buf.Count;
    WriteU32(buf, 0x02014b50);                 // central header
    WriteU16(buf, 45);                         // version made by
    WriteU16(buf, 45);                         // version needed
    WriteU16(buf, 0);                          // flags
    WriteU16(buf, 0);                          // method
    WriteU16(buf, 0);                          // mod time
    WriteU16(buf, 0x21);                       // mod date
    WriteU32(buf, crc);
    WriteU32(buf, 0xFFFFFFFF);                 // comp size = сентинел → в extra
    WriteU32(buf, 0xFFFFFFFF);                 // uncomp size = сентинел → в extra
    WriteU16(buf, (ushort)name.Length);
    WriteU16(buf, 28);                         // extra len (4 + 3×8)
    WriteU16(buf, 0);                          // comment len
    WriteU16(buf, 0);                          // disk start
    WriteU16(buf, 0);                          // internal attrs
    WriteU32(buf, 0);                          // external attrs
    WriteU32(buf, 0xFFFFFFFF);                 // local offset = сентинел → в extra
    buf.AddRange(name);
    WriteU16(buf, 0x0001);                     // ZIP64 extra id
    WriteU16(buf, 24);                         // размер данных extra
    WriteU64(buf, (ulong)content.Length);      // uncompressed (порядок по APPNOTE)
    WriteU64(buf, (ulong)content.Length);      // compressed
    WriteU64(buf, (ulong)localOffset);         // offset
    long cdSize = buf.Count - cdStart;

    long zip64EocdOffset = buf.Count;
    WriteU32(buf, 0x06064b50);                 // ZIP64 EOCD record
    WriteU64(buf, 44);                         // размер записи (56 - 12)
    WriteU16(buf, 45);
    WriteU16(buf, 45);
    WriteU32(buf, 0);                          // disk number
    WriteU32(buf, 0);                          // disk with CD
    WriteU64(buf, 1);                          // entries this disk
    WriteU64(buf, 1);                          // total entries
    WriteU64(buf, (ulong)cdSize);
    WriteU64(buf, (ulong)cdStart);

    WriteU32(buf, 0x07064b50);                 // ZIP64 EOCD locator
    WriteU32(buf, 0);                          // disk with ZIP64 EOCD
    WriteU64(buf, (ulong)zip64EocdOffset);
    WriteU32(buf, 1);                          // total disks

    WriteU32(buf, 0x06054b50);                 // EOCD
    WriteU16(buf, 0);
    WriteU16(buf, 0);
    WriteU16(buf, 0xFFFF);                     // entries this disk = сентинел
    WriteU16(buf, 0xFFFF);                     // total entries = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD size = сентинел
    WriteU32(buf, 0xFFFFFFFF);                 // CD offset = сентинел
    WriteU16(buf, 0);                          // comment len

    byte[] fixture = buf.ToArray();

    // Фикстура валидна, если её читает независимый BCL-ридер.
    using (var za = new ZipArchive(new MemoryStream(fixture, writable: false), ZipArchiveMode.Read))
    {
      ZipArchiveEntry e = Assert.Single(za.Entries);
      Assert.Equal("z64.txt", e.FullName);
      Assert.Equal(content.Length, e.Length);

      using Stream s = e.Open();
      using var read = new MemoryStream();
      s.CopyTo(read);
      Assert.Equal(content, read.ToArray());
    }

    // Наш потоковый ридер берёт размеры/смещение из ZIP64 extra.
    using var ms = new MemoryStream(fixture, writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));

    ZipStreamEntry entry = Assert.Single(entries);
    Assert.Equal("z64.txt", entry.Name);
    Assert.Equal(content.Length, entry.UncompressedSize);
    Assert.Equal(content.Length, entry.CompressedSize);
    Assert.Equal(0, entry.LocalHeaderOffset);
    Assert.Equal(crc, entry.Crc);
    Assert.Equal(0, entry.Method);
  }

  private static void WriteU16(List<byte> buf, ushort v)
  {
    buf.Add((byte)v);
    buf.Add((byte)(v >> 8));
  }

  private static void WriteU32(List<byte> buf, uint v)
  {
    for (int i = 0; i < 4; i++)
      buf.Add((byte)(v >> (i * 8)));
  }

  private static void WriteU64(List<byte> buf, ulong v)
  {
    for (int i = 0; i < 8; i++)
      buf.Add((byte)(v >> (i * 8)));
  }

  private static byte[] BuildBclZip(params (string Name, byte[] Content)[] files)
  {
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach ((string name, byte[] content) in files)
      {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream s = entry.Open();
        s.Write(content, 0, content.Length);
      }
    }

    return ms.ToArray();
  }

  // Поток без произвольного доступа — для проверки требования seekable.
  private sealed class ForwardOnlyStream(byte[] data) : Stream
  {
    private readonly MemoryStream _inner = new(data, writable: false);

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
      if (disposing)
        _inner.Dispose();
      base.Dispose(disposing);
    }
  }
}
