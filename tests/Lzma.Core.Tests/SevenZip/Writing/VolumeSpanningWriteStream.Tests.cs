using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потока записи по томам: чистая побайтовая нарезка (границы точные, склейка = оригинал) и
/// сквозная проверка — потоковый LZMA2-архив, записанный по томам, склеивается и распаковывается.
/// </summary>
public sealed class VolumeSpanningWriteStreamTests
{
  [Fact]
  public void Имена_Томов_ТриЦифры()
  {
    Assert.Equal(@"C:\out.7z.001", VolumeSpanningWriteStream.VolumePath(@"C:\out.7z", 0));
    Assert.Equal(@"C:\out.7z.002", VolumeSpanningWriteStream.VolumePath(@"C:\out.7z", 1));
    Assert.Equal(@"C:\out.7z.010", VolumeSpanningWriteStream.VolumePath(@"C:\out.7z", 9));
  }

  [Fact]
  public void СыраяЗапись_РежетПоГранице_СклейкаРавнаОригиналу()
  {
    string dir = NewDir();
    try
    {
      string basePath = Path.Combine(dir, "data.bin");
      var payload = new byte[10_000];
      for (int i = 0; i < payload.Length; i++)
        payload[i] = (byte)(i * 31 + 7);

      const long volSize = 4096;
      using (var vs = new VolumeSpanningWriteStream(basePath, volSize))
      {
        // Пишем разными кусками, чтобы задеть границы томов внутри одной записи.
        vs.Write(payload, 0, 1000);
        vs.Write(payload, 1000, 5000); // пересекает границу 4096
        vs.Write(payload, 6000, 4000);
        Assert.Equal(3, vs.VolumeCount); // 10000 / 4096 → 3 тома
      }

      Assert.Equal(volSize, new FileInfo(VolumeSpanningWriteStream.VolumePath(basePath, 0)).Length);
      Assert.Equal(volSize, new FileInfo(VolumeSpanningWriteStream.VolumePath(basePath, 1)).Length);
      Assert.Equal(10_000 - 2 * volSize, new FileInfo(VolumeSpanningWriteStream.VolumePath(basePath, 2)).Length);

      Assert.Equal(payload, JoinVolumes(basePath));
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  [Fact]
  public void ПотоковыйLzma2_ПоТомам_СклейкаРаспаковывается()
  {
    string dir = NewDir();
    try
    {
      // Плохо сжимаемое содержимое, чтобы архив гарантированно превысил размер тома.
      byte[] a = Rng(9000, 0x1111);
      byte[] b = Rng(7000, 0x2222);

      var entries = new List<SevenZipStreamingEntry>
      {
        new("a.bin", a.LongLength, () => new MemoryStream(a)),
        new("dir/b.bin", b.LongLength, () => new MemoryStream(b)),
      };

      string basePath = Path.Combine(dir, "out.7z");
      const long volSize = 2048;

      using (var vs = new VolumeSpanningWriteStream(basePath, volSize))
      {
        Assert.Equal(SevenZipArchiveWriteResult.Ok,
            SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, vs, 1 << 20));
        Assert.True(vs.VolumeCount >= 2, $"ожидалось несколько томов, получили {vs.VolumeCount}");
      }

      // Все тома кроме последнего — ровно volSize.
      var paths = new List<string>();
      for (int i = 0; File.Exists(VolumeSpanningWriteStream.VolumePath(basePath, i)); i++)
        paths.Add(VolumeSpanningWriteStream.VolumePath(basePath, i));
      for (int i = 0; i < paths.Count - 1; i++)
        Assert.Equal(volSize, new FileInfo(paths[i]).Length);

      // Склейка распаковывается (успех = сигнатура пропатчена в .001 корректно).
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(JoinVolumes(basePath), out SevenZipDecodedEntry[] decoded));
      Assert.Equal(2, decoded.Length);
      Assert.Equal(a, decoded[0].Bytes);
      Assert.Equal(b, decoded[1].Bytes);
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private static byte[] Rng(int length, uint seed)
  {
    var d = new byte[length];
    uint x = seed;
    for (int i = 0; i < length; i++) { x = x * 1664525u + 1013904223u; d[i] = (byte)(x >> 24); }
    return d;
  }

  [Fact]
  public void МелкийАрхив_ОдинТом_Распаковывается()
  {
    string dir = NewDir();
    try
    {
      byte[] a = Encoding.UTF8.GetBytes("маленький");
      var entries = new List<SevenZipStreamingEntry> { new("a.txt", a.LongLength, () => new MemoryStream(a)) };

      string basePath = Path.Combine(dir, "small.7z");
      using (var vs = new VolumeSpanningWriteStream(basePath, 10L << 20)) // 10 МБ том — влезает целиком
      {
        Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, vs, 1 << 20));
        Assert.Equal(1, vs.VolumeCount);
      }

      Assert.True(File.Exists(VolumeSpanningWriteStream.VolumePath(basePath, 0)));
      Assert.False(File.Exists(VolumeSpanningWriteStream.VolumePath(basePath, 1)));

      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.DecodeToEntries(JoinVolumes(basePath), out SevenZipDecodedEntry[] decoded));
      Assert.Equal(a, Assert.Single(decoded).Bytes);
    }
    finally { Directory.Delete(dir, recursive: true); }
  }

  private static byte[] JoinVolumes(string basePath)
  {
    using var joined = new MemoryStream();
    for (int i = 0; ; i++)
    {
      string path = VolumeSpanningWriteStream.VolumePath(basePath, i);
      if (!File.Exists(path))
        break;
      joined.Write(File.ReadAllBytes(path));
    }
    return joined.ToArray();
  }

  private static string NewDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "LzmaVolumes", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    return dir;
  }
}
