using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты интеграции BCJ2 в диспетчер BuildArchive и в in-memory Auto: метод Bcj2 round-trip'ится;
/// Auto выбирает BCJ2 для набора x86-исполняемых (PE), но НЕ для текста/смешанного набора.
/// </summary>
public sealed class SevenZipArchiveWriterBcj2AutoTests
{
  // Синтетический x86/x64 PE: сигнатура MZ + указатель на PE\0\0 (machine i386) + call-heavy тело.
  private static byte[] MakePeExecutable(int length, uint target)
  {
    var d = new byte[System.Math.Max(length, 0x100)];
    d[0] = (byte)'M';
    d[1] = (byte)'Z';

    const int peOff = 0x80;
    d[0x3C] = peOff; // e_lfanew = 0x80 (влезает в один байт)
    d[peOff] = (byte)'P';
    d[peOff + 1] = (byte)'E';
    d[peOff + 2] = 0;
    d[peOff + 3] = 0;
    d[peOff + 4] = 0x4C; // machine = 0x014C (i386)
    d[peOff + 5] = 0x01;

    for (int p = 0x100; p + 8 < d.Length; p += 50)
    {
      d[p] = 0xE8; // CALL rel32 → фиксированный абсолютный target (BCJ2 сожмёт почти в ноль)
      uint rel = unchecked(target - (uint)p - 5);
      d[p + 1] = (byte)rel;
      d[p + 2] = (byte)(rel >> 8);
      d[p + 3] = (byte)(rel >> 16);
      d[p + 4] = (byte)(rel >> 24);
    }

    return d;
  }

  [Fact]
  public void BuildArchive_МетодBcj2_RoundTrip()
  {
    byte[] content = MakePeExecutable(20000, 0x40);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("app.exe", content)], SevenZipWriterCompressionMethod.Bcj2, out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(content, Assert.Single(entries).Bytes);
  }

  [Fact]
  public void Auto_НаборИсполняемых_ВыбираетBcj2()
  {
    byte[] a = MakePeExecutable(20000, 0x40);
    byte[] b = MakePeExecutable(12000, 0x100);
    SevenZipArchiveWriterEntry[] set =
    [
        new("app.exe", a),
        new("lib.dll", b),
    ];

    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(set, SevenZipWriterCompressionMethod.Auto, out byte[] autoArchive));
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildBcj2Archive(set, out byte[] bcj2Archive));

    // Auto для чистого набора исполняемых должен дать РОВНО тот же архив, что явный BCJ2.
    Assert.Equal(bcj2Archive, autoArchive);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(autoArchive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(2, entries.Length);
  }

  [Fact]
  public void Auto_Текст_НеВыбираетBcj2()
  {
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("обычный текст про дома. ", 2000)));
    SevenZipArchiveWriterEntry[] set = [new("doc.txt", text)];

    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(set, SevenZipWriterCompressionMethod.Auto, out byte[] autoArchive));
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildBcj2Archive(set, out byte[] bcj2Archive));

    Assert.NotEqual(bcj2Archive, autoArchive); // выбрал PPMd/LZMA2, не BCJ2

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(autoArchive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(text, Assert.Single(entries).Bytes);
  }

  [Fact]
  public void Auto_Смешанный_ИсполняемыйПлюсТекст_НеВыбираетBcj2()
  {
    byte[] exe = MakePeExecutable(15000, 0x40);
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("текстовый файл рядом. ", 1500)));
    SevenZipArchiveWriterEntry[] set =
    [
        new("app.exe", exe),
        new("readme.txt", text),
    ];

    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(set, SevenZipWriterCompressionMethod.Auto, out byte[] autoArchive));
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildBcj2Archive(set, out byte[] bcj2Archive));

    // Не все файлы исполняемые → BCJ2 на весь архив не применяем (шаг 2 — пофайлово).
    Assert.NotEqual(bcj2Archive, autoArchive);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(autoArchive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(2, entries.Length);
  }
}
