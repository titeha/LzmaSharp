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
