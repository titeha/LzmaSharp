using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового пофайлового BCJ2 (шаг 2): потоковый Auto выбирает BCJ2 для x86-исполняемых,
/// пишет много-стримовый folder (4 coder-а) в общий потоковый пайплайн; смешанный набор
/// (BCJ2 + PPMd + Copy в соседних folder-ах) round-trip'ится; выход читается настоящим 7-Zip.
/// </summary>
public sealed class SevenZipArchiveWriterBcj2StreamingTests
{
  private static byte[] MakePeExecutable(int length, uint target)
  {
    var d = new byte[Math.Max(length, 0x100)];
    d[0] = (byte)'M';
    d[1] = (byte)'Z';

    const int peOff = 0x80;
    d[0x3C] = peOff;
    d[peOff] = (byte)'P';
    d[peOff + 1] = (byte)'E';
    d[peOff + 4] = 0x4C; // machine i386
    d[peOff + 5] = 0x01;

    for (int p = 0x100; p + 8 < d.Length; p += 50)
    {
      d[p] = 0xE8;
      uint rel = unchecked(target - (uint)p - 5);
      d[p + 1] = (byte)rel;
      d[p + 2] = (byte)(rel >> 8);
      d[p + 3] = (byte)(rel >> 16);
      d[p + 4] = (byte)(rel >> 24);
    }

    return d;
  }

  [Fact]
  public void ChooseAuto_PE_ВыбираетBcj2()
  {
    byte[] pe = MakePeExecutable(8000, 0x40);
    Assert.Equal(SevenZipWriterCompressionMethod.Bcj2, SevenZipArchiveWriter.ChooseAutoMethodForBytes(pe));
  }

  [Fact]
  public void ПотоковыйAuto_PE_RoundTrip()
  {
    byte[] pe = MakePeExecutable(30000, 0x40);
    var entries = new List<SevenZipStreamingEntry> { new("app.exe", pe.LongLength, () => new MemoryStream(pe)) };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, ms, 1 << 20));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));
    Assert.Equal(pe, Assert.Single(decoded).Bytes);
  }

  [Fact]
  public void ПотоковыйAuto_Смешанный_PE_Текст_Случайное_RoundTrip()
  {
    byte[] pe = MakePeExecutable(20000, 0x40);
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("текст и слова про дома. ", 3000)));
    var random = new byte[200_000];
    uint x = 0x2468ACE0;
    for (int i = 0; i < random.Length; i++) { x = x * 1664525u + 1013904223u; random[i] = (byte)(x >> 24); }

    var entries = new List<SevenZipStreamingEntry>
    {
      new("bin/app.exe", pe.LongLength, () => new MemoryStream(pe)),   // → BCJ2 (4 coder-а)
      new("doc.txt", text.LongLength, () => new MemoryStream(text)),   // → PPMd
      new("blob.bin", random.LongLength, () => new MemoryStream(random)), // → Copy
      new("dir", 0, () => new MemoryStream([]), IsDirectory: true),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, ms, 1 << 20));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));

    Assert.Equal(4, decoded.Length);
    Assert.Equal(pe, decoded[0].Bytes);
    Assert.Equal(text, decoded[1].Bytes);
    Assert.Equal(random, decoded[2].Bytes);
    Assert.True(decoded[3].IsDirectory);
  }

  [Fact]
  public void ПотоковыйBcj2Метод_НесколькоФайлов_RoundTrip()
  {
    // Явный метод BCJ2: применяется к КАЖДОМУ непустому файлу (в т.ч. не исполняемому — без вреда).
    byte[] pe = MakePeExecutable(15000, 0x40);
    byte[] plain = Encoding.UTF8.GetBytes("не исполняемый — но BCJ2 обратим и здесь");
    var entries = new List<SevenZipStreamingEntry>
    {
      new("app.exe", pe.LongLength, () => new MemoryStream(pe)),
      new("notes.txt", plain.LongLength, () => new MemoryStream(plain)),
    };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildBcj2ArchiveToStream(entries, ms));

    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), out SevenZipDecodedEntry[] decoded));
    Assert.Equal(2, decoded.Length);
    Assert.Equal(pe, decoded[0].Bytes);
    Assert.Equal(plain, decoded[1].Bytes);
  }

  [Fact]
  public void ПотоковыйAuto_Bcj2_Читается7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return;

    byte[] pe = MakePeExecutable(40000, 0x5A5A);
    var entries = new List<SevenZipStreamingEntry> { new("app.exe", pe.LongLength, () => new MemoryStream(pe)) };

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAutoArchiveToStream(entries, ms, 1 << 20));

    string dir = Path.Combine(Path.GetTempPath(), "bcj2stream_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string archivePath = Path.Combine(dir, "out.7z");
      File.WriteAllBytes(archivePath, ms.ToArray());

      Assert.Equal(0, Run(sevenZip, $"t \"{archivePath}\""));
      Assert.Equal(0, Run(sevenZip, $"e \"{archivePath}\" -o\"{dir}\" -y"));
      Assert.Equal(pe, File.ReadAllBytes(Path.Combine(dir, "app.exe")));
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
}
