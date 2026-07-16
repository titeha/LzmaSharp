using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты параллели ПО ФАЙЛАМ в потоковом writer-е: мелкие файлы сжимаются параллельно (волнами),
/// большие — блочно; архив round-trip'ится и детерминирован (нет гонок, порядок packed сохранён).
/// </summary>
public sealed class SevenZipArchiveWriterParallelFilesTests
{
  private static byte[] Text(string s, int rep) => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(s, rep)));

  private static List<SevenZipStreamingEntry> ManyFiles()
  {
    var list = new List<SevenZipStreamingEntry>();
    // 50 мелких файлов (волны) + пара крупнее блока (>4 МиБ по умолчанию dict).
    for (int i = 0; i < 50; i++)
    {
      byte[] d = Text($"регион {i} данные 0123456789 ", 200 + i * 10);
      list.Add(new($"reg/f{i:D3}.xml", d.LongLength, () => new MemoryStream(d)));
    }
    byte[] big = Text("большой файл 0123456789abcdef ", 400_000); // ~11 МБ > блока
    list.Add(new("big.bin", big.LongLength, () => new MemoryStream(big)));
    return list;
  }

  private static byte[] Build(List<SevenZipStreamingEntry> entries, int dict)
  {
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, ms, dict));
    return ms.ToArray();
  }

  [Fact]
  public void МногоФайлов_RoundTrip()
  {
    var entries = ManyFiles();
    byte[] archive = Build(entries, 1 << 22);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] decoded));

    Assert.Equal(entries.Count, decoded.Length);
    for (int i = 0; i < entries.Count; i++)
    {
      Assert.Equal(entries[i].Name, decoded[i].Name);
      using var s = entries[i].OpenRead();
      using var buf = new MemoryStream();
      s.CopyTo(buf);
      Assert.Equal(buf.ToArray(), decoded[i].Bytes);
    }
  }

  [Fact]
  public void Детерминизм_ДвеСборкиИдентичны()
  {
    // Параллель по файлам не должна вносить гонок: два прогона дают байт-идентичный архив.
    var e1 = ManyFiles();
    var e2 = ManyFiles();
    Assert.Equal(Build(e1, 1 << 22), Build(e2, 1 << 22));
  }
}
