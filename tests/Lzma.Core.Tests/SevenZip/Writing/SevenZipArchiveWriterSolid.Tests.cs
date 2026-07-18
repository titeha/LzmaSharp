using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip.Writing;

/// <summary>
/// Solid-запись 7z: файлы группы склеиваются в один folder с под-потоками (SubStreamsInfo). Проверяем
/// round-trip нашим декодером (per-file размеры/CRC/имена) для Copy/LZMA2/PPMd и ВЫГОДУ плотности
/// (solid жмёт много похожих файлов плотнее пофайлового — модель копит статистику).
/// </summary>
public sealed class SevenZipArchiveWriterSolidTests
{
  private static SevenZipStreamingEntry File(string name, byte[] content)
      => new(name, content.LongLength, () => new MemoryStream(content, writable: false));

  private const int Dict = 1 << 20;

  [Fact]
  public void SolidCopy_НесколькоФайлов_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes("первый файл");
    byte[] b = Encoding.UTF8.GetBytes("второй, подлиннее, файл с данными");
    byte[] c = Encoding.UTF8.GetBytes("третий");

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildCopySolidArchiveToStream(
        [File("a.txt", a), File("dir/b.txt", b), File("c.txt", c)], ms));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(3, decoded.Length);
    Assert.Equal("a.txt", decoded[0].Name); Assert.Equal(a, decoded[0].Bytes);
    Assert.Equal("dir/b.txt", decoded[1].Name); Assert.Equal(b, decoded[1].Bytes);
    Assert.Equal("c.txt", decoded[2].Name); Assert.Equal(c, decoded[2].Bytes);
  }

  [Fact]
  public void SolidLzma2_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("alpha ", 2000)));
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("beta ", 2000)));

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2SolidArchiveToStream(
        [File("a.txt", a), File("b.txt", b)], ms, Dict));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(2, decoded.Length);
    Assert.Equal(a, decoded[0].Bytes);
    Assert.Equal(b, decoded[1].Bytes);
  }

  [Fact]
  public void SolidPpmd_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("текстовые данные ", 1500)));
    byte[] b = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("ещё текст для PPMd ", 1500)));

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildPpmdSolidArchiveToStream(
        [File("a.txt", a), File("b.txt", b)], ms));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(2, decoded.Length);
    Assert.Equal(a, decoded[0].Bytes);
    Assert.Equal(b, decoded[1].Bytes);
  }

  [Fact]
  public void AutoSolid_СмешанныйНабор_ГруппируетПоКодекамИRoundTrip()
  {
    var rnd = new Random(11);
    var entries = new List<SevenZipStreamingEntry>();
    var expected = new Dictionary<string, byte[]>();

    // Текстовые (→ PPMd) и несжимаемые случайные (→ Copy) — разные кодек-группы.
    for (int i = 0; i < 8; i++)
    {
      byte[] text = Encoding.UTF8.GetBytes($"текстовый документ {i} " + string.Concat(Enumerable.Repeat("слова слова слова ", 50)));
      entries.Add(File($"text/doc{i}.txt", text));
      expected[$"text/doc{i}.txt"] = text;

      byte[] noise = new byte[2000];
      rnd.NextBytes(noise);
      entries.Add(File($"bin/blob{i}.dat", noise));
      expected[$"bin/blob{i}.dat"] = noise;
    }

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoSolidArchiveToStream(entries, ms, Dict));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(expected.Count, decoded.Length);
    foreach (SevenZipDecodedEntry d in decoded)
      Assert.Equal(expected[d.Name], d.Bytes);
  }

  [Fact]
  public void AutoSolid_ПлотнееПофайловогоAuto()
  {
    var entries = new List<SevenZipStreamingEntry>();
    for (int i = 0; i < 60; i++)
    {
      byte[] content = Encoding.UTF8.GetBytes($"документ №{i}. " + string.Concat(Enumerable.Repeat("повторяющийся текст для PPMd ", 40)));
      entries.Add(File($"f{i}.txt", content));
    }

    using var perFile = new MemoryStream();
    using var solid = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, perFile, Dict));
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoSolidArchiveToStream(entries, solid, Dict));

    Assert.True(solid.Length < perFile.Length,
        $"auto-solid={solid.Length} должен быть меньше auto-per-file={perFile.Length}");

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(solid.ToArray(), out SevenZipDecodedEntry[] decoded));
    Assert.Equal(60, decoded.Length);
  }

  [Fact]
  public void Solid_МногоПохожихФайлов_ПлотнееПофайлового()
  {
    // 50 похожих небольших текстовых файлов: пофайлово каждый стартует «холодным», solid — плотнее.
    var entries = new List<SevenZipStreamingEntry>();
    for (int i = 0; i < 50; i++)
    {
      byte[] content = Encoding.UTF8.GetBytes($"файл №{i}: " + string.Concat(Enumerable.Repeat("повторяющийся текст ", 40)));
      entries.Add(File($"f{i}.txt", content));
    }

    using var perFile = new MemoryStream();
    using var solid = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, perFile, Dict));
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2SolidArchiveToStream(entries, solid, Dict));

    // Solid заметно меньше (одна модель на все файлы против модели-на-файл).
    Assert.True(solid.Length < perFile.Length,
        $"solid={solid.Length} должен быть меньше per-file={perFile.Length}");

    // И корректно распаковывается.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(solid.ToArray(), out SevenZipDecodedEntry[] decoded));
    Assert.Equal(50, decoded.Length);
  }
}
