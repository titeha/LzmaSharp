using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового Auto (пофайловый автовыбор PPMd/LZMA2): смешанный контент round-trip'ится
/// (у каждого folder-а свой coder), текстовый файл идёт в PPMd (плотнее).
/// </summary>
public sealed class SevenZipArchiveWriterAutoStreamingTests
{
  [Fact]
  public void Auto_СмешанныйКонтент_RoundTrip()
  {
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Обычный текстовый абзац про адреса и дома. ", 4000)));
    var random = new byte[300_000];
    uint s = 0x1357;
    for (int i = 0; i < random.Length; i++) { s = s * 1664525u + 1013904223u; random[i] = (byte)(s >> 24); }

    var entries = new List<SevenZipStreamingEntry>
    {
      new("doc.txt", text.LongLength, () => new MemoryStream(text)),      // → PPMd
      new("blob.bin", random.LongLength, () => new MemoryStream(random)), // → LZMA2
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, ms, 1 << 20));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(3, decoded.Length);
    Assert.Equal("doc.txt", decoded[0].Name);
    Assert.Equal(text, decoded[0].Bytes);
    Assert.Equal("blob.bin", decoded[1].Name);
    Assert.Equal(random, decoded[1].Bytes);
    Assert.True(decoded[2].IsDirectory);
  }

  [Fact]
  public void Auto_Текст_ПлотнееЧемLZMA2()
  {
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Естественный текст, слова и предложения. ", 8000)));
    var entries = new List<SevenZipStreamingEntry> { new("t.txt", text.LongLength, () => new MemoryStream(text)) };

    using var autoMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, autoMs, 1 << 20));

    using var lzmaMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, lzmaMs, 1 << 20));

    // На тексте Auto выберет PPMd → должен быть НЕ больше (обычно меньше) LZMA2.
    Assert.True(autoMs.Length <= lzmaMs.Length,
        $"Auto(PPMd)={autoMs.Length} должно быть <= LZMA2={lzmaMs.Length}");
  }
}
