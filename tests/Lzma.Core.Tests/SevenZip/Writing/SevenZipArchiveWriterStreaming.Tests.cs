using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потоковой записи .7z в Stream (BuildLzma2ArchiveToStream): архив собирается без удержания
/// файлов/архива в памяти и корректно распаковывается нашим декодером (round-trip).
/// </summary>
public sealed class SevenZipArchiveWriterStreamingTests
{
  private static SevenZipStreamingEntry Data(string name, byte[] bytes)
      => new(name, bytes.LongLength, () => new MemoryStream(bytes));

  [Fact]
  public void ПотоковаяЗапись_МногоФайлов_Вложенность_Пустые_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Поток в архив 0123456789 ", 8000))); // многочанковый
    byte[] b = Encoding.UTF8.GetBytes("мир");

    var entries = new List<SevenZipStreamingEntry>
    {
      new("dir", 0, () => throw new IOException("не должно открываться"), IsDirectory: true),
      Data("a.txt", a),
      new("empty.txt", 0, () => new MemoryStream([])),
      Data("dir/big.bin", big),
      Data("b.txt", b),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, ms, dictionarySize: 1 << 20));

    byte[] archive = ms.ToArray();

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));

    Assert.Equal(entries.Count, decoded.Length);

    for (int i = 0; i < entries.Count; i++)
      Assert.Equal(entries[i].Name, decoded[i].Name);

    // Директория/пустой файл.
    Assert.True(decoded[0].IsDirectory);
    Assert.Empty(decoded[2].Bytes);

    // Данные — байт-в-байт.
    Assert.Equal(a, decoded[1].Bytes);
    Assert.Equal(big, decoded[3].Bytes);
    Assert.Equal(b, decoded[4].Bytes);
  }

  [Fact]
  public void ПотоковаяЗапись_ОдинБольшойФайл_RoundTrip_ЧерезФайлНаДиске()
  {
    // Пишем архив в ФАЙЛ (seekable FileStream) — как в реальном сценарии.
    byte[] data = new byte[300_000];
    uint state = 0xABCDEF01;
    for (int i = 0; i < data.Length; i++)
    {
      state = state * 1664525u + 1013904223u;
      data[i] = (byte)((state >> 24) ^ (i & 0x0F)); // смесь: часть сжимаемо, часть нет
    }

    string dir = Path.Combine(Path.GetTempPath(), "LzmaWriterStreaming", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string archivePath = Path.Combine(dir, "out.7z");

    try
    {
      var entries = new List<SevenZipStreamingEntry> { Data("blob.bin", data) };

      using (var fs = new FileStream(archivePath, FileMode.Create, FileAccess.ReadWrite))
      {
        Assert.Equal(SevenZipArchiveWriteResult.Ok,
            SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, fs, dictionarySize: 1 << 16));
      }

      byte[] archive = File.ReadAllBytes(archivePath);

      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));

      Assert.Single(decoded);
      Assert.Equal("blob.bin", decoded[0].Name);
      Assert.Equal(data, decoded[0].Bytes);
    }
    finally
    {
      try { Directory.Delete(dir, recursive: true); } catch { }
    }
  }

  [Fact]
  public void ПотоковаяЗапись_НеseekableВыход_NotSupported()
  {
    var entries = new List<SevenZipStreamingEntry> { Data("a.txt", Encoding.UTF8.GetBytes("x")) };

    using var nonSeekable = new NonSeekableWriteStream();
    Assert.Equal(SevenZipArchiveWriteResult.NotSupported,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, nonSeekable, dictionarySize: 1 << 16));
  }

  private sealed class NonSeekableWriteStream : Stream
  {
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override bool CanRead => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) { }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
  }
}
