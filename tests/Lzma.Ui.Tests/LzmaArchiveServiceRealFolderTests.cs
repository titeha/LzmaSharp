using System.Text;
using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Xunit;

namespace Lzma.Ui.Tests;

public sealed class LzmaArchiveServiceRealFolderTests
{
  [Fact]
  public void РеальнаяПапка_ЧерезПеречисление_СоздатьОткрыть()
  {
    string root = Path.Combine(Path.GetTempPath(), "lzs-realfolder-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.Combine(root, "sub"));
    Directory.CreateDirectory(Path.Combine(root, "sub", "deep"));

    var rnd = new Random(9);
    // Разнообразные файлы: текст (PPMd), бинарь низкой энтропии (LZMA2), шум (Copy), exe (BCJ2), пустой, специмена
    File.WriteAllText(Path.Combine(root, "readme.txt"), string.Concat(Enumerable.Repeat("обычный текстовый документ ", 300)));
    File.WriteAllText(Path.Combine(root, "файл с пробелом и юникодом.txt"), string.Concat(Enumerable.Repeat("текст ", 200)));
    File.WriteAllText(Path.Combine(root, "sub", "notes.md"), string.Concat(Enumerable.Repeat("# заметки\nстрока ", 200)));
    File.WriteAllText(Path.Combine(root, "sub", "deep", new string('n', 150) + ".txt"), "короткий");
    File.WriteAllBytes(Path.Combine(root, "empty.dat"), []);
    byte[] lz = new byte[9000]; for (int i = 0; i < lz.Length; i++) lz[i] = (byte)(i % 4); File.WriteAllBytes(Path.Combine(root, "sub", "low.bin"), lz);
    byte[] noise = new byte[9000]; rnd.NextBytes(noise); File.WriteAllBytes(Path.Combine(root, "noise.rnd"), noise);
    byte[] exe = new byte[12000]; exe[0]=(byte)'M'; exe[1]=(byte)'Z'; exe[0x3C]=0x40; exe[0x40]=(byte)'P'; exe[0x41]=(byte)'E'; exe[0x44]=0x4C; exe[0x45]=0x01;
    for (int p=0x100;p+5<exe.Length;p+=33){exe[p]=0xE8;exe[p+1]=(byte)p;} File.WriteAllBytes(Path.Combine(root, "app.exe"), exe);

    var browser = new DesktopFileSystemBrowser();
    var sources = browser.EnumerateForArchive([root]);
    var entries = new List<SevenZipStreamingEntry>();
    foreach (var s in sources)
      entries.Add(new SevenZipStreamingEntry(s.EntryName, s.Length, () => browser.OpenRead(s.FullPath)));

    var svc = new LzmaArchiveService();
    string path = Path.Combine(root, "..", "archive_auto_repro.7z");
    try
    {
      var cr = svc.CreateArchiveToFileAsync(entries, path, SevenZipWriterCompressionMethod.Auto, 1 << 20).Result;
      Assert.Equal(SevenZipArchiveWriteResult.Ok, cr);

      var opened = svc.OpenAsync(File.ReadAllBytes(path), null).Result;
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);
    }
    finally
    {
      try { File.Delete(path); } catch {}
      try { Directory.Delete(root, true); } catch {}
    }
  }
}
