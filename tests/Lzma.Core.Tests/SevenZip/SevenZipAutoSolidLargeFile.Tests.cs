using System.Text;
using Lzma.Core.SevenZip;
using Xunit;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Auto-solid: файл больше порога кодируется СВОИМ потоковым folder-ом по классифицированному кодеку
/// (не солидится, в память не читается). Порог понижен, чтобы прогнать путь на маленьких файлах.
/// Round-trip нашим декодером; смешанный набор (solid-блоки мелких + потоковые большие) сохраняет
/// имена/содержимое.
/// </summary>
public sealed class SevenZipAutoSolidLargeFileTests
{
  private static SevenZipStreamingEntry Entry(string name, byte[] data)
      => new(name, data.Length, () => new MemoryStream(data));

  private static byte[] BuildAuto(IReadOnlyList<SevenZipStreamingEntry> entries, long threshold)
  {
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAutoSolidArchiveToStream(entries, ms, 1 << 20, 1, null, default, null, threshold));
    return ms.ToArray();
  }

  private static byte[] Text(int n)
      => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("текстовый документ строка данных ", n / 33 + 1)).Substring(0, n));

  private static byte[] Noise(int n, int seed)
  {
    byte[] b = new byte[n];
    new Random(seed).NextBytes(b);
    return b;
  }

  private static void AssertRoundTrip(byte[] archive, params (string Name, byte[] Data)[] expected)
  {
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));
    foreach ((string name, byte[] data) in expected)
      Assert.Equal(data, decoded.First(e => e.Name.EndsWith(name, StringComparison.Ordinal)).Bytes);
  }

  [Fact]
  public void БольшойТекст_ПотоковыйPPMd()
  {
    byte[] doc = Text(6000);
    byte[] archive = BuildAuto([Entry("big.txt", doc)], threshold: 100);
    AssertRoundTrip(archive, ("big.txt", doc));
  }

  [Fact]
  public void БольшойНесжимаемый_ПотоковыйCopy()
  {
    byte[] noise = Noise(7000, 3);
    byte[] archive = BuildAuto([Entry("big.bin", noise)], threshold: 100);
    AssertRoundTrip(archive, ("big.bin", noise));
  }

  [Fact]
  public void Смешанный_МелкиеSolidИБольшиеПотоком()
  {
    byte[] t1 = Text(400), t2 = Text(500);      // мелкий текст → solid PPMd-блок
    byte[] bigText = Text(9000);                // большой текст → потоковый PPMd folder
    byte[] smallNoise = Noise(300, 1);          // мелкий шум → Copy
    byte[] bigNoise = Noise(8000, 2);           // большой шум → потоковый Copy folder

    byte[] archive = BuildAuto(
        [Entry("a.txt", t1), Entry("b.txt", t2), Entry("huge.txt", bigText), Entry("s.bin", smallNoise), Entry("huge.bin", bigNoise)],
        threshold: 1000);

    AssertRoundTrip(archive,
        ("a.txt", t1), ("b.txt", t2), ("huge.txt", bigText), ("s.bin", smallNoise), ("huge.bin", bigNoise));
  }

  [Fact]
  public void БезПорога_ВсёКакРаньше_Solid()
  {
    // threshold = int.MaxValue (дефолт) → большого пути нет, всё солидится как прежде.
    byte[] t1 = Text(2000), t2 = Text(3000);
    byte[] archive = BuildAuto([Entry("a.txt", t1), Entry("b.txt", t2)], threshold: int.MaxValue);
    AssertRoundTrip(archive, ("a.txt", t1), ("b.txt", t2));
  }
}
