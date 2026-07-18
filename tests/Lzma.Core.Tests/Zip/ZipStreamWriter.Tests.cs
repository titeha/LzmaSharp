using System.IO.Compression;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковая запись ZIP в Stream: round-trip нашим читателем/извлекателем, независимая проверка BCL
/// <see cref="ZipArchive"/>, ZIP64 при большом числе записей и отчёт о прогрессе.
/// </summary>
public sealed class ZipStreamWriterTests
{
  private static ZipStreamingEntry File(string name, byte[] content)
      => new(name, content.LongLength, () => new MemoryStream(content, writable: false));

  private static ZipStreamingEntry Dir(string name)
      => new(name, 0, () => Stream.Null, IsDirectory: true);

  [Fact]
  public void Запись_RoundTrip_НашимИзвлекателем_StoreИDeflate()
  {
    byte[] text = Encoding.UTF8.GetBytes("streaming zip writer round-trip");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("compress me ", 5000)));   // Deflate
    var rnd = new Random(3);
    byte[] noise = new byte[4000];
    rnd.NextBytes(noise);                                                                            // Store

    ZipStreamingEntry[] entries =
    [
        Dir("dir/"),
        File("dir/readme.txt", text),
        File("dir/sub/big.txt", big),
        File("noise.bin", noise),
    ];

    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(entries, ms));

    byte[] archive = ms.ToArray();

    // Наш потоковый извлекатель.
    string dest = NewTempDir();
    try
    {
      using var read = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] listed));
      Assert.Equal(ZipExtractResult.Ok, ZipStreamExtractor.ExtractToDirectory(read, listed, dest));

      Assert.Equal(text, System.IO.File.ReadAllBytes(Path.Combine(dest, "dir", "readme.txt")));
      Assert.Equal(big, System.IO.File.ReadAllBytes(Path.Combine(dest, "dir", "sub", "big.txt")));
      Assert.Equal(noise, System.IO.File.ReadAllBytes(Path.Combine(dest, "noise.bin")));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  [Fact]
  public void Запись_ЧитаетсяBcl_БайтСовместимо()
  {
    byte[] a = Encoding.UTF8.GetBytes("hello bcl");
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("xyz ", 4000)));

    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write([File("a.txt", a), File("d/b.txt", b)], ms));

    using var za = new ZipArchive(new MemoryStream(ms.ToArray(), writable: false), ZipArchiveMode.Read);

    ZipArchiveEntry ea = za.GetEntry("a.txt")!;
    Assert.Equal(a, ReadEntry(ea));

    ZipArchiveEntry eb = za.GetEntry("d/b.txt")!;
    Assert.Equal(b, ReadEntry(eb));
  }

  [Fact]
  public void Запись_ZIP64_МногоЗаписей_ЧитаетсяНамиИBcl()
  {
    const int count = 70_000; // > 65535 → счётчик EOCD переполняется, нужен ZIP64
    var entries = new ZipStreamingEntry[count];
    for (int i = 0; i < count; i++)
      entries[i] = File($"f{i}.txt", []); // пустые → архив компактный

    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(entries, ms));

    byte[] archive = ms.ToArray();

    // Наш читатель.
    using (var read = new MemoryStream(archive, writable: false))
    {
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] listed));
      Assert.Equal(count, listed.Length);
    }

    // Независимо — BCL.
    using var za = new ZipArchive(new MemoryStream(archive, writable: false), ZipArchiveMode.Read);
    Assert.Equal(count, za.Entries.Count);
  }

  [Fact]
  public void Запись_РепортитПрогресс_МонотонноДоИтога()
  {
    byte[] a = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("alpha ", 3000)));
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("beta ", 3000)));
    long total = a.LongLength + b.LongLength;

    var reports = new List<SevenZipProgress>();
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(
        [File("a.txt", a), File("b.txt", b)], ms, new SyncProgress(reports.Add)));

    Assert.NotEmpty(reports);
    for (int i = 1; i < reports.Count; i++)
      Assert.True(reports[i].BytesProcessed >= reports[i - 1].BytesProcessed);
    Assert.All(reports, r => Assert.Equal(total, r.TotalBytes));
    Assert.Equal(total, reports[^1].BytesProcessed);
  }

  [Fact]
  public void Запись_МногоФайлов_ПараллельноДетерминированно_RoundTrip()
  {
    // Много файлов → несколько параллельных волн. Выход должен быть детерминирован (без гонок).
    var rnd = new Random(2026);
    var entries = new ZipStreamingEntry[200];
    var payloads = new byte[200][];
    for (int i = 0; i < entries.Length; i++)
    {
      byte[] p = i % 2 == 0
          ? Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"file{i} ", 300))) // Deflate
          : new byte[500];
      if (i % 2 == 1) rnd.NextBytes(p);                                                 // Store
      payloads[i] = p;
      entries[i] = File($"dir{i % 7}/f{i}.dat", p);
    }

    using var ms1 = new MemoryStream();
    using var ms2 = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(entries, ms1));
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(entries, ms2));

    Assert.Equal(ms1.ToArray(), ms2.ToArray()); // детерминизм

    // И round-trip нашим извлекателем.
    string dest = NewTempDir();
    try
    {
      using var read = new MemoryStream(ms1.ToArray(), writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] listed));
      Assert.Equal(entries.Length, listed.Length);
      Assert.Equal(ZipExtractResult.Ok, ZipStreamExtractor.ExtractToDirectory(read, listed, dest));

      for (int i = 0; i < entries.Length; i++)
        Assert.Equal(payloads[i], System.IO.File.ReadAllBytes(Path.Combine(dest, $"dir{i % 7}", $"f{i}.dat")));
    }
    finally
    {
      if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
    }
  }

  [Fact]
  public void Запись_НеSeekableВыход_InvalidData()
  {
    using var forward = new ForwardOnlyWriteStream();
    Assert.Equal(ZipWriteResult.InvalidData, ZipStreamWriter.Write([File("a.txt", [1, 2, 3])], forward));
  }

  private static byte[] ReadEntry(ZipArchiveEntry entry)
  {
    using Stream s = entry.Open();
    using var buffer = new MemoryStream();
    s.CopyTo(buffer);
    return buffer.ToArray();
  }

  private static string NewTempDir()
      => Path.Combine(Path.GetTempPath(), "lzs-zipw-" + System.Guid.NewGuid().ToString("N"));

  private sealed class SyncProgress(System.Action<SevenZipProgress> onReport) : System.IProgress<SevenZipProgress>
  {
    public void Report(SevenZipProgress value) => onReport(value);
  }

  private sealed class ForwardOnlyWriteStream : Stream
  {
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}
