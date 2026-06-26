using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

public sealed class SevenZipArchiveWriterAesEncryptionTests
{
  private static byte[] Iv16(byte seed)
  {
    byte[] iv = new byte[16];
    for (int i = 0; i < 16; i++)
      iv[i] = (byte)(i + seed);
    return iv;
  }

  private static SevenZipDecodedEntry[] DecodeWithPassword(byte[] archive, string password)
  {
    using SevenZipPassword pw = SevenZipPassword.FromString(password);
    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive, SevenZipDecodeOptions.WithPassword(pw), out SevenZipDecodedEntry[] entries);

    Assert.Equal(SevenZipArchiveDecodeResult.Ok, result);
    return entries;
  }

  [Fact]
  public void BuildAes_ОдинФайл_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes("Секретное содержимое под AES-256. 0123456789.");

    using SevenZipPassword pw = SevenZipPassword.FromString("p@ssw0rd");
    var options = new SevenZipAesEncryptionOptions
    {
      Password = pw,
      NumCyclesPower = 4,
      Salt = [1, 2, 3, 4],
      InitializationVector = Iv16(0x10),
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesEncryptedArchive(
        [new SevenZipArchiveWriterEntry("secret.txt", content)], options, out byte[] archive));

    SevenZipDecodedEntry entry = Assert.Single(DecodeWithPassword(archive, "p@ssw0rd"));
    Assert.Equal("secret.txt", entry.Name);
    Assert.Equal(content, entry.Bytes);
  }

  [Fact]
  public void BuildAes_НесколькоФайлов_RoundTrip()
  {
    byte[] a = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("alpha ", 500)));
    byte[] b = Encoding.UTF8.GetBytes("beta");

    using SevenZipPassword pw = SevenZipPassword.FromString("multi");
    var options = new SevenZipAesEncryptionOptions
    {
      Password = pw,
      NumCyclesPower = 4,
      Salt = [9, 9],
      InitializationVector = Iv16(0x30),
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesEncryptedArchive(
        [
            new SevenZipArchiveWriterEntry("a.txt", a),
            new SevenZipArchiveWriterEntry("dir/b.txt", b),
        ],
        options, out byte[] archive));

    SevenZipDecodedEntry[] entries = DecodeWithPassword(archive, "multi");
    Assert.Equal(2, entries.Length);
    Assert.Equal(a, entries.Single(e => e.Name.Replace('\\', '/') == "a.txt").Bytes);
    Assert.Equal(b, entries.Single(e => e.Name.Replace('\\', '/') == "dir/b.txt").Bytes);
  }

  [Fact]
  public void BuildAes_СоСжатиемLzma2_RoundTrip()
  {
    byte[] content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("сжимаемый текст под шифром ", 400)));

    using SevenZipPassword pw = SevenZipPassword.FromString("zip+enc");
    var options = new SevenZipAesEncryptionOptions
    {
      Password = pw,
      NumCyclesPower = 4,
      Salt = [7, 7, 7],
      InitializationVector = Iv16(0x50),
      CompressWithLzma2 = true,
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesEncryptedArchive(
        [new SevenZipArchiveWriterEntry("doc.txt", content)], options, out byte[] archive));

    // Со сжатием архив заметно меньше открытого текста.
    Assert.True(archive.Length < content.Length);

    SevenZipDecodedEntry entry = Assert.Single(DecodeWithPassword(archive, "zip+enc"));
    Assert.Equal(content, entry.Bytes);
  }

  [Fact]
  public void BuildAes_НеверныйПароль_НеОткрывается()
  {
    byte[] content = Encoding.UTF8.GetBytes("очень секретно");

    using SevenZipPassword pw = SevenZipPassword.FromString("correct");
    var options = new SevenZipAesEncryptionOptions
    {
      Password = pw,
      NumCyclesPower = 4,
      Salt = [5, 5, 5, 5],
      InitializationVector = Iv16(0x70),
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesEncryptedArchive(
        [new SevenZipArchiveWriterEntry("s.txt", content)], options, out byte[] archive));

    using SevenZipPassword wrong = SevenZipPassword.FromString("wrong");
    SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.DecodeToEntries(
        archive, SevenZipDecodeOptions.WithPassword(wrong), out _);

    Assert.NotEqual(SevenZipArchiveDecodeResult.Ok, result);
  }

  [Fact]
  public void BuildAes_РаспаковываетсяНастоящим7Zip()
  {
    const string sevenZip = @"C:\Program Files\7-Zip\7z.exe";
    if (!File.Exists(sevenZip))
      return;

    byte[] content = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(
        "AES ↔ настоящий 7-Zip. The quick brown fox. 0123456789. ", 200)));

    using SevenZipPassword pw = SevenZipPassword.FromString("Secret123");
    var options = new SevenZipAesEncryptionOptions
    {
      Password = pw,
      NumCyclesPower = 8,
      CompressWithLzma2 = true,
    };

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesEncryptedArchive(
        [new SevenZipArchiveWriterEntry("payload.txt", content)], options, out byte[] archive));

    string dir = Path.Combine(Path.GetTempPath(), "aeslive_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
      string archivePath = Path.Combine(dir, "out.7z");
      File.WriteAllBytes(archivePath, archive);

      Assert.Equal(0, Run(sevenZip, $"t \"{archivePath}\" -pSecret123"));
      Assert.Equal(0, Run(sevenZip, $"e \"{archivePath}\" -o\"{dir}\" -y -pSecret123"));

      byte[] extracted = File.ReadAllBytes(Path.Combine(dir, "payload.txt"));
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
}
