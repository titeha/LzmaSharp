using System.Text;
using Lzma.Core.SevenZip;
using Xunit;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Потоковый PPMd-путь для больших файлов в 7z-writer (BuildPpmdArchiveToStream): порог понижен, чтобы
/// маленькие файлы шли ПОТОКОВЫМ путём (Ppmd7Encoder.Encode(Stream)). Выход байт-в-байт совпадает с
/// пофайловым (in-memory) путём (тот же кодек), архив читается нашим декодером.
/// </summary>
public sealed class SevenZipPpmdStreamingLargeTests
{
  private static SevenZipStreamingEntry Entry(string name, byte[] data)
      => new(name, data.Length, () => new MemoryStream(data));

  private static byte[] BuildPpmd(IReadOnlyList<SevenZipStreamingEntry> entries, long threshold)
  {
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildPpmdArchiveToStream(entries, ms, null, default, null, 1, threshold));
    return ms.ToArray();
  }

  private static byte[] Text(int n)
      => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("PPMd streaming payload line. ", n / 28 + 1)).Substring(0, n));

  [Fact]
  public void ПотоковыйПуть_БайтВБайт_КакПофайловый()
  {
    var entries = new[] { Entry("a.txt", Text(5000)), Entry("b.txt", Text(9000)) };

    byte[] normal = BuildPpmd(entries, threshold: int.MaxValue); // все мелкие → пофайловый путь
    byte[] streamed = BuildPpmd(entries, threshold: 10);         // порог 10 → все идут потоковым путём

    Assert.Equal(normal, streamed); // тот же кодек → архив байт-в-байт
  }

  [Fact]
  public void ПотоковыйПуть_RoundTrip()
  {
    byte[] a = Text(4000), b = Text(12000);
    byte[] archive = BuildPpmd([Entry("docs/a.txt", a), Entry("docs/b.txt", b)], threshold: 10);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));

    Assert.Equal(a, decoded.First(e => e.Name.EndsWith("a.txt", StringComparison.Ordinal)).Bytes);
    Assert.Equal(b, decoded.First(e => e.Name.EndsWith("b.txt", StringComparison.Ordinal)).Bytes);
  }

  [Fact]
  public void Смешанный_МелкийВолнойИБольшойПотоком_RoundTrip()
  {
    byte[] small = Text(300), big = Text(8000);
    // Порог 1000: small (300) → волна, big (8000) → потоковый путь.
    byte[] archive = BuildPpmd([Entry("small.txt", small), Entry("big.txt", big)], threshold: 1000);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));

    Assert.Equal(small, decoded.First(e => e.Name == "small.txt").Bytes);
    Assert.Equal(big, decoded.First(e => e.Name == "big.txt").Bytes);
  }
}
