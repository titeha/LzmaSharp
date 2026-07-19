using System.Text;
using Lzma.Core.SevenZip;
using Xunit;

namespace Lzma.Core.Tests.SevenZip.Writing;

/// <summary>
/// Пофайловые потоковые методы (Copy/BCJ2/PPMd) жмут файлы ПАРАЛЛЕЛЬНО, но пишут по порядку — выход
/// должен быть детерминирован (без гонок) и корректно распаковываться.
/// </summary>
public sealed class SevenZipPerFileParallelTests
{
  private static SevenZipStreamingEntry F(string name, byte[] c)
      => new(name, c.LongLength, () => new MemoryStream(c, writable: false));

  private static byte[] Pe(int seed)
  {
    var b = new byte[8000 + seed % 500];
    b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3C] = 0x40; b[0x40] = (byte)'P'; b[0x41] = (byte)'E'; b[0x44] = 0x4C; b[0x45] = 0x01;
    for (int p = 0x100; p + 5 < b.Length; p += 30 + seed % 7) { b[p] = 0xE8; b[p + 1] = (byte)(p + seed); }
    return b;
  }

  public static IEnumerable<object[]> Methods()
  {
    yield return ["Copy"];
    yield return ["Ppmd"];
    yield return ["Bcj2"];
  }

  [Theory]
  [MemberData(nameof(Methods))]
  public void Детерминизм_И_RoundTrip(string method)
  {
    var rnd = new Random(7);
    var entries = new List<SevenZipStreamingEntry>();
    var payloads = new Dictionary<string, byte[]>();
    for (int i = 0; i < 40; i++)
    {
      byte[] p = method switch
      {
        "Ppmd" => Encoding.UTF8.GetBytes($"документ {i} " + string.Concat(Enumerable.Repeat("текст ", 60))),
        "Bcj2" => Pe(i),
        _ => Rnd(rnd, 1500),
      };
      string name = $"dir{i % 5}/f{i}.dat";
      entries.Add(F(name, p));
      payloads[name] = p;
    }

    byte[] Build()
    {
      using var ms = new MemoryStream();
      var r = method switch
      {
        "Ppmd" => SevenZipArchiveWriter.BuildPpmdArchiveToStream(entries, ms),
        "Bcj2" => SevenZipArchiveWriter.BuildBcj2ArchiveToStream(entries, ms),
        _ => SevenZipArchiveWriter.BuildCopyArchiveToStream(entries, ms),
      };
      Assert.Equal(SevenZipArchiveWriteResult.Ok, r);
      return ms.ToArray();
    }

    byte[] a1 = Build();
    byte[] a2 = Build();
    Assert.Equal(a1, a2); // детерминизм (нет гонок)

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.DecodeToEntries(a1, out var decoded));
    Assert.Equal(entries.Count, decoded.Length);
    foreach (var d in decoded)
      Assert.Equal(payloads[d.Name], d.Bytes);
  }

  private static byte[] Rnd(Random r, int n) { var b = new byte[n]; r.NextBytes(b); return b; }
}
