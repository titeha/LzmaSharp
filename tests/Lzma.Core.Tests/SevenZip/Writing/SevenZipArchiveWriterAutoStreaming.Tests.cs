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

/// <summary>
/// Тесты адаптивного store в Auto: практически несжимаемые (высокоэнтропийные — уже сжатые/случайные)
/// файлы выбираются под Copy, а не гоняются через LZMA2; текст/умеренный бинарь — как раньше.
/// </summary>
public sealed class SevenZipArchiveWriterAutoStoreTests
{
  [Fact]
  public void Выбор_Случайное_Copy_Текст_Ppmd_Бинарь_Lzma2()
  {
    // Случайные байты — высокая энтропия → Copy.
    var random = new byte[400_000];
    uint s = 0xABCDEF01;
    for (int i = 0; i < random.Length; i++) { s = s * 1664525u + 1013904223u; random[i] = (byte)(s >> 24); }
    Assert.Equal(SevenZipWriterCompressionMethod.Copy, SevenZipArchiveWriter.ChooseAutoMethodForBytes(random));

    // Натуральный текст — низкая энтропия, мало «бинарных» → PPMd.
    byte[] text = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("обычные слова и предложения про дома. ", 5000)));
    Assert.Equal(SevenZipWriterCompressionMethod.Ppmd, SevenZipArchiveWriter.ChooseAutoMethodForBytes(text));

    // Умеренно-структурный бинарь (много нулей/повторов) — сжимаем, не текст → LZMA2.
    var structured = new byte[200_000];
    for (int i = 0; i < structured.Length; i++) structured[i] = (byte)(i % 7 == 0 ? (i & 0x1F) : 0);
    Assert.Equal(SevenZipWriterCompressionMethod.Lzma2, SevenZipArchiveWriter.ChooseAutoMethodForBytes(structured));
  }

  [Fact]
  public void Auto_Несжимаемое_ХранитсяCopy_RoundTripИНеБольшеОригинала()
  {
    var random = new byte[500_000];
    uint s = 0x2468ACE0;
    for (int i = 0; i < random.Length; i++) { s = s * 1664525u + 1013904223u; random[i] = (byte)(s >> 24); }

    var entries = new List<SevenZipStreamingEntry> { new("blob.bin", random.LongLength, () => new MemoryStream(random)) };

    using var autoMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, autoMs, 1 << 20));

    // Round-trip.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(autoMs.ToArray(), out SevenZipDecodedEntry[] decoded));
    Assert.Single(decoded);
    Assert.Equal(random, decoded[0].Bytes);

    // Store не должен раздувать: Auto(Copy) на несжимаемом <= LZMA2 (у LZMA2 накладные на чанки).
    using var lzmaMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, lzmaMs, 1 << 20));
    Assert.True(autoMs.Length <= lzmaMs.Length, $"Auto(store)={autoMs.Length} <= LZMA2={lzmaMs.Length}");
  }
}
