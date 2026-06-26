using System.Diagnostics;
using System.IO;
using System.Linq;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterBcj2Tests
{
  // Полу-реалистичный x86-поток: случайный фон + регулярные E8/E9 с короткими смещениями.
  private static byte[] MakeX86Like(int length, uint seed)
  {
    byte[] data = new byte[length];
    uint x = seed;

    for (int i = 0; i < length; i++)
    {
      x ^= x << 13;
      x ^= x >> 17;
      x ^= x << 5;
      data[i] = (byte)x;
    }

    for (int i = 16; i + 8 < length; i += 29)
    {
      data[i] = (i % 2 == 0) ? (byte)0xE8 : (byte)0xE9;
      int rel = (i * 5) % 512;
      data[i + 1] = (byte)rel;
      data[i + 2] = (byte)(rel >> 8);
      data[i + 3] = 0;
      data[i + 4] = 0;
    }

    return data;
  }

  [Fact]
  public void BuildBcj2_ОдинФайл_RoundTrip()
  {
    byte[] content = MakeX86Like(4096, 0xABCDEF01);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [new SevenZipArchiveWriterEntry("app.bin", content)], out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    SevenZipDecodedEntry entry = Assert.Single(entries);
    Assert.Equal("app.bin", entry.Name);
    Assert.Equal(content, entry.Bytes);
  }

  [Fact]
  public void BuildBcj2_НесколькоФайлов_RoundTrip()
  {
    byte[] a = MakeX86Like(2000, 0x11111111);
    byte[] b = MakeX86Like(5000, 0x22222222);
    byte[] c = MakeX86Like(1, 0x33333333);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [
            new SevenZipArchiveWriterEntry("a.bin", a),
            new SevenZipArchiveWriterEntry("dir/b.bin", b),
            new SevenZipArchiveWriterEntry("c.bin", c),
        ],
        out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    Assert.Equal(3, entries.Length);
    Assert.Equal(a, entries.Single(e => e.Name.Replace('\\', '/') == "a.bin").Bytes);
    Assert.Equal(b, entries.Single(e => e.Name.Replace('\\', '/') == "dir/b.bin").Bytes);
    Assert.Equal(c, entries.Single(e => e.Name.Replace('\\', '/') == "c.bin").Bytes);
  }

  [Fact]
  public void BuildBcj2_ЖмётПлотнееLzma2_НаПотокеВызовов()
  {
    // «Исполняемый» поток: фон нулей + 1000 CALL (E8) к одному абсолютному адресу.
    // В сыром виде смещения у всех разные (rel = T - ip), плохо сжимаются обычным LZMA2;
    // после BCJ2-конвертации они становятся одним и тем же абсолютным адресом → почти ноль.
    const int length = 60000;
    const uint target = 0x40;

    byte[] content = new byte[length];
    for (int p = 100; p + 8 < length; p += 50)
    {
      content[p] = 0xE8;
      uint rel = unchecked(target - (uint)p - 5); // abs = rel + p + 5 = target
      content[p + 1] = (byte)rel;
      content[p + 2] = (byte)(rel >> 8);
      content[p + 3] = (byte)(rel >> 16);
      content[p + 4] = (byte)(rel >> 24);
    }

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [new SevenZipArchiveWriterEntry("app.exe", content)], out byte[] bcj2Archive));

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("app.exe", content)], SevenZipWriterCompressionMethod.Lzma2, out byte[] lzma2Archive));

    // BCJ2 заметно компактнее обычного LZMA2 на таком потоке.
    Assert.True(bcj2Archive.Length < lzma2Archive.Length,
        $"ожидался выигрыш BCJ2: bcj2={bcj2Archive.Length}, lzma2={lzma2Archive.Length}");

    // И при этом корректно распаковывается.
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(bcj2Archive, out SevenZipDecodedEntry[] entries));
    Assert.Equal(content, Assert.Single(entries).Bytes);
  }

  [Fact]
  public void BuildBcj2_РаспаковываетсяНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return; // Настоящий 7-Zip недоступен в этом окружении.

    byte[] content = MakeX86Like(40000, 0x5A5A5A5A);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [new SevenZipArchiveWriterEntry("app.exe", content)], out byte[] archive));

    string dir = Path.Combine(Path.GetTempPath(), "bcj2live_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string archivePath = Path.Combine(dir, "out.7z");
      File.WriteAllBytes(archivePath, archive);

      Assert.Equal(0, Run(sevenZip, $"t \"{archivePath}\""));
      Assert.Equal(0, Run(sevenZip, $"e \"{archivePath}\" -o\"{dir}\" -y"));

      byte[] extracted = File.ReadAllBytes(Path.Combine(dir, "app.exe"));
      Assert.Equal(content, extracted);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }
  }

  private static int Run(string exe, string args)
  {
    var psi = new ProcessStartInfo(exe, args)
    {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };

    using var p = Process.Start(psi)!;
    p.StandardOutput.ReadToEnd();
    p.StandardError.ReadToEnd();
    p.WaitForExit();
    return p.ExitCode;
  }

  [Fact]
  public void BuildBcj2_СмешанныйСПустыми_RoundTrip()
  {
    byte[] content = MakeX86Like(3000, 0x44444444);

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2Archive(
        [
            new SevenZipArchiveWriterEntry("readme", []),                 // пустой файл
            new SevenZipArchiveWriterEntry("bin/app.exe", content),       // непустой → BCJ2
            new SevenZipArchiveWriterEntry("emptydir", [], IsDirectory: true),
        ],
        out byte[] archive));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(archive, out SevenZipDecodedEntry[] entries));

    Assert.Equal(3, entries.Length);
    Assert.Equal(content, entries.Single(e => e.Name.Replace('\\', '/') == "bin/app.exe").Bytes);
  }
}
