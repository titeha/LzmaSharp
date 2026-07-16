using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Тесты потокового 7zAES (AES-1): пофайлово LZMA2→AES; архив расшифровывается нашим декодером с
/// верным паролем, отвергается с неверным, и читается настоящим 7-Zip с паролем.
/// </summary>
public sealed class SevenZipArchiveWriterAesStreamingTests
{
  private static List<SevenZipStreamingEntry> TwoFiles(out byte[] a, out byte[] b)
  {
    a = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("секретные данные 12345 ", 500)));
    byte[] local = a;
    b = Encoding.UTF8.GetBytes("второй файл под шифром");
    byte[] localB = b;
    return new List<SevenZipStreamingEntry>
    {
      new("a.txt", local.LongLength, () => new MemoryStream(local)),
      new("dir/b.txt", localB.LongLength, () => new MemoryStream(localB)),
    };
  }

  private static SevenZipAesEncryptionOptions Options(string password) => new()
  {
    Password = SevenZipPassword.FromString(password),
    CompressWithLzma2 = true,
  };

  [Fact]
  public void ПотоковыйAes_RoundTrip_ВерныйПароль()
  {
    var entries = TwoFiles(out byte[] a, out byte[] b);

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAesArchiveToStream(entries, ms, Options("secret123"), 1 << 20));

    var options = SevenZipDecodeOptions.WithPassword(SevenZipPassword.FromString("secret123"));
    Assert.Equal(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), options, out SevenZipDecodedEntry[] decoded));

    Assert.Equal(2, decoded.Length);
    Assert.Equal(a, decoded[0].Bytes);
    Assert.Equal(b, decoded[1].Bytes);
  }

  [Fact]
  public void ПотоковыйAes_НеверныйПароль_НеРаспаковывается()
  {
    var entries = TwoFiles(out _, out _);

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAesArchiveToStream(entries, ms, Options("correct-horse"), 1 << 20));

    var wrong = SevenZipDecodeOptions.WithPassword(SevenZipPassword.FromString("battery-staple"));
    Assert.NotEqual(SevenZipArchiveDecodeResult.Ok,
        SevenZipArchiveDecoder.DecodeToEntries(ms.ToArray(), wrong, out _));
  }

  [Fact]
  public void ПотоковыйAes_Читается7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return;

    var entries = TwoFiles(out byte[] a, out _);
    const string password = "p@ssw0rd-Тест";

    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildAesArchiveToStream(entries, ms, Options(password), 1 << 20));

    string dir = Path.Combine(Path.GetTempPath(), "aesstream_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string archivePath = Path.Combine(dir, "out.7z");
      File.WriteAllBytes(archivePath, ms.ToArray());

      Assert.Equal(0, Run(sevenZip, $"t -p{password} \"{archivePath}\""));
      Assert.Equal(0, Run(sevenZip, $"e -p{password} \"{archivePath}\" -o\"{dir}\" -y"));
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(dir, "a.txt")));
    }
    finally { Directory.Delete(dir, recursive: true); }
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
