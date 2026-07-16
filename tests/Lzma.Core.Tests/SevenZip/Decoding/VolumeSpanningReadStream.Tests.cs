using System;
using System.Collections.Generic;
using System.IO;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потока чтения по томам: склейка = оригинал (с произвольными Seek), сквозной цикл
/// «запись по томам → чтение склейкой → list/extract», детекция первого тома (.001).
/// </summary>
public sealed class VolumeSpanningReadStreamTests
{
  [Fact]
  public void Чтение_СклейкаРавнаОригиналу_СПроизвольнымиSeek()
  {
    string dir = NewDir();
    try
    {
      string basePath = Path.Combine(dir, "data.bin");
      byte[] payload = Rng(10_000, 0x9999);

      using (var w = new VolumeSpanningWriteStream(basePath, 4096))
        w.Write(payload, 0, payload.Length);

      using var r = new VolumeSpanningReadStream(basePath);
      Assert.Equal(payload.Length, r.Length);

      // Полное чтение подряд.
      byte[] all = ReadAll(r);
      Assert.Equal(payload, all);

      // Seek в конец (как reader за next-header) и назад через границу тома.
      r.Position = payload.Length - 100;
      byte[] tail = new byte[100];
      Assert.Equal(100, r.Read(tail, 0, 100));
      Assert.Equal(payload[^100..], tail);

      r.Position = 4090; // около границы первого тома
      byte[] cross = new byte[20];
      ReadExact(r, cross);
      Assert.Equal(payload[4090..4110], cross);
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  [Fact]
  public void Сквозной_ЗаписьПоТомам_ЧтениеСклейкой_ListИExtract()
  {
    string dir = NewDir();
    try
    {
      byte[] a = Rng(9000, 0x1234);
      byte[] b = Rng(6000, 0x5678);
      var entries = new List<SevenZipStreamingEntry>
      {
        new("a.bin", a.LongLength, () => new MemoryStream(a)),
        new("sub/b.bin", b.LongLength, () => new MemoryStream(b)),
      };

      string basePath = Path.Combine(dir, "arc.7z");
      using (var w = new VolumeSpanningWriteStream(basePath, 2048))
        Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, w, 1 << 20));

      // List через склейку.
      using (var r = new VolumeSpanningReadStream(basePath))
      {
        Assert.True(r.VolumeCount >= 2);
        Assert.Equal(SevenZipArchiveDecodeResult.Ok,
            SevenZipArchiveDecoder.ListEntriesFromStream(r, out SevenZipListedEntry[] listed));
        Assert.Equal(2, listed.Length);
      }

      // Extract через склейку.
      string outDir = Path.Combine(dir, "out");
      using (var r = new VolumeSpanningReadStream(basePath))
      {
        Assert.Equal(SevenZipArchiveDecodeResult.Ok,
            SevenZipArchiveDecoder.ExtractToDirectoryFromStream(r, SevenZipDecodeOptions.Default, outDir, overwrite: false));
      }

      Assert.Equal(a, File.ReadAllBytes(Path.Combine(outDir, "a.bin")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(outDir, "sub", "b.bin")));
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  [Fact]
  public void Детекция_ПервогоТома()
  {
    string dir = NewDir();
    try
    {
      string basePath = Path.Combine(dir, "arc.7z");
      File.WriteAllBytes(VolumeSpanningWriteStream.VolumePath(basePath, 0), [1, 2, 3]);

      // .001 при наличии → база.
      Assert.True(VolumeSpanningReadStream.TryGetVolumeBasePath(basePath + ".001", out string b1));
      Assert.Equal(basePath, b1);

      // Обычный путь без .NNN → не том.
      Assert.False(VolumeSpanningReadStream.TryGetVolumeBasePath(basePath, out _));

      // .001 без файла рядом → не том.
      Assert.False(VolumeSpanningReadStream.TryGetVolumeBasePath(Path.Combine(dir, "nope.7z.001"), out _));
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private static byte[] ReadAll(Stream s)
  {
    using var ms = new MemoryStream();
    s.CopyTo(ms);
    return ms.ToArray();
  }

  private static void ReadExact(Stream s, byte[] buf)
  {
    int off = 0;
    while (off < buf.Length)
    {
      int n = s.Read(buf, off, buf.Length - off);
      if (n <= 0) throw new EndOfStreamException();
      off += n;
    }
  }

  private static byte[] Rng(int length, uint seed)
  {
    var d = new byte[length];
    uint x = seed;
    for (int i = 0; i < length; i++) { x = x * 1664525u + 1013904223u; d[i] = (byte)(x >> 24); }
    return d;
  }

  private static string NewDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaVolumesR", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
