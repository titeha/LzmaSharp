using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового чтения СТРУКТУРЫ архива (SevenZipArchiveStreamReader): сигнатура + next-header
/// читаются из Stream без загрузки packed-данных → можно открыть большой архив. Сверка со span-reader.
/// </summary>
public sealed class SevenZipArchiveStreamReaderTests
{
  private static byte[] BuildArchive()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Поток header 0123456789 ", 4000)));

    var entries = new List<SevenZipStreamingEntry>
    {
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
      new("a.txt", a.LongLength, () => new MemoryStream(a)),
      new("big.bin", big.LongLength, () => new MemoryStream(big)),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, ms, 1 << 20));
    return ms.ToArray();
  }

  [Fact]
  public void ЧитаетHeaderИзStream_СовпадаетСоSpanReader()
  {
    byte[] archive = BuildArchive();

    using var stream = new MemoryStream(archive);
    SevenZipArchiveDecodeResult r = SevenZipArchiveStreamReader.ReadHeader(stream, out SevenZipHeader header, out long packedBase);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, r);
    Assert.Equal(SevenZipSignatureHeader.Size, packedBase); // packed сразу после сигнатуры

    // Сверка со span-reader (эталон).
    var spanReader = new SevenZipArchiveReader();
    Assert.Equal(SevenZipArchiveReadResult.Ok, spanReader.Read(archive, out _));
    Assert.True(spanReader.Header.HasValue);

    Assert.Equal(spanReader.Header.Value.FilesInfo.FileCount, header.FilesInfo.FileCount);
    Assert.Equal(3UL, header.FilesInfo.FileCount);
    Assert.True(header.FilesInfo.HasNames);
    Assert.Equal(new[] { "dir", "a.txt", "big.bin" }, header.FilesInfo.Names);
  }

  [Fact]
  public void БитаяСигнатура_InvalidData()
  {
    byte[] archive = BuildArchive();
    archive[0] ^= 0xFF; // портим сигнатуру

    using var stream = new MemoryStream(archive);
    Assert.Equal(SevenZipArchiveDecodeResult.InvalidData,
        SevenZipArchiveStreamReader.ReadHeader(stream, out _, out _));
  }

  [Fact]
  public void НеseekableStream_NotSupported()
  {
    byte[] archive = BuildArchive();
    using var nonSeekable = new NonSeekableReadStream(archive);
    Assert.Equal(SevenZipArchiveDecodeResult.NotSupported,
        SevenZipArchiveStreamReader.ReadHeader(nonSeekable, out _, out _));
  }

  private sealed class NonSeekableReadStream(byte[] data) : Stream
  {
    private readonly MemoryStream _inner = new(data);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
  }
}
