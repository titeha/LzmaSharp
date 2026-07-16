using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Регресс: потоковое извлечение (ExtractToDirectoryFromStream / DecodeFolderStreamToStream) обязано
/// поддерживать ВСЕ folder-ы, которые производит потоковый Auto — PPMd, BCJ2, Copy, LZMA2 — а не
/// только одиночный LZMA2. Раньше не-LZMA2 folder давал NotSupported (баг на реальном 301-МБ архиве).
/// </summary>
public sealed class SevenZipStreamingExtractMixedCodecsTests
{
  private static byte[] Pe(int length, uint target)
  {
    var d = new byte[Math.Max(length, 0x100)];
    d[0] = (byte)'M'; d[1] = (byte)'Z';
    d[0x3C] = 0x80;
    d[0x80] = (byte)'P'; d[0x81] = (byte)'E'; d[0x84] = 0x4C; d[0x85] = 0x01;
    for (int p = 0x100; p + 8 < d.Length; p += 50)
    {
      d[p] = 0xE8;
      uint rel = unchecked(target - (uint)p - 5);
      d[p + 1] = (byte)rel; d[p + 2] = (byte)(rel >> 8); d[p + 3] = (byte)(rel >> 16); d[p + 4] = (byte)(rel >> 24);
    }
    return d;
  }

  private static byte[] Rng(int length, uint seed)
  {
    var d = new byte[length];
    uint x = seed;
    for (int i = 0; i < length; i++) { x = x * 1664525u + 1013904223u; d[i] = (byte)(x >> 24); }
    return d;
  }

  [Fact]
  public void ПотоковоеИзвлечение_СмесиКодеков_ВсеФайлыЦелы()
  {
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("обычные слова про адреса и дома. ", 4000)));
    byte[] pe = Pe(25000, 0x40);
    byte[] random = Rng(300_000, 0xABCD);
    byte[] structured = new byte[120_000];
    for (int i = 0; i < structured.Length; i++) structured[i] = (byte)(i % 7 == 0 ? (i & 0x1F) : 0);

    // Проверяем, что набор действительно задействует все четыре кодека.
    Assert.Equal(SevenZipWriterCompressionMethod.Ppmd, SevenZipArchiveWriter.ChooseAutoMethodForBytes(text));
    Assert.Equal(SevenZipWriterCompressionMethod.Bcj2, SevenZipArchiveWriter.ChooseAutoMethodForBytes(pe));
    Assert.Equal(SevenZipWriterCompressionMethod.Copy, SevenZipArchiveWriter.ChooseAutoMethodForBytes(random));
    Assert.Equal(SevenZipWriterCompressionMethod.Lzma2, SevenZipArchiveWriter.ChooseAutoMethodForBytes(structured));

    var entries = new List<SevenZipStreamingEntry>
    {
      new("doc.txt", text.LongLength, () => new MemoryStream(text)),
      new("bin/app.exe", pe.LongLength, () => new MemoryStream(pe)),
      new("blob.bin", random.LongLength, () => new MemoryStream(random)),
      new("data.dat", structured.LongLength, () => new MemoryStream(structured)),
    };

    using var archive = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, archive, 1 << 20));

    // Ключевое: извлекаем ПОТОКОВЫМ путём (как «Извлечь архив с диска…» / тома).
    string dir = Path.Combine(Path.GetTempPath(), "LzmaMixExtract", Guid.NewGuid().ToString("N"));
    try
    {
      archive.Position = 0;
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectoryFromStream(archive, SevenZipDecodeOptions.Default, dir, overwrite: false));

      Assert.Equal(text, File.ReadAllBytes(Path.Combine(dir, "doc.txt")));
      Assert.Equal(pe, File.ReadAllBytes(Path.Combine(dir, "bin", "app.exe")));
      Assert.Equal(random, File.ReadAllBytes(Path.Combine(dir, "blob.bin")));
      Assert.Equal(structured, File.ReadAllBytes(Path.Combine(dir, "data.dat")));
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }
}
