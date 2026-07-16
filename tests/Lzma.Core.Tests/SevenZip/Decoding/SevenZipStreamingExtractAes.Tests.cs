using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// AES-3: потоковое извлечение зашифрованных архивов. Детекция шифрования по заголовку; извлечение
/// из потока с верным паролем распаковывает, без пароля — нет; незашифрованный не помечается.
/// </summary>
public sealed class SevenZipStreamingExtractAesTests
{
  private static MemoryStream BuildAes(string password, out byte[] a, out byte[] b)
  {
    a = Encoding.UTF8.GetBytes(string.Concat(System.Linq.Enumerable.Repeat("шифрованный текст ", 400)));
    byte[] la = a;
    b = Encoding.UTF8.GetBytes("второй под шифром");
    byte[] lb = b;
    var entries = new List<SevenZipStreamingEntry>
    {
      new("a.txt", la.LongLength, () => new MemoryStream(la)),
      new("sub/b.txt", lb.LongLength, () => new MemoryStream(lb)),
    };
    var options = new SevenZipAesEncryptionOptions { Password = SevenZipPassword.FromString(password), CompressWithLzma2 = true };
    var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildAesArchiveToStream(entries, ms, options, 1 << 20));
    return ms;
  }

  [Fact]
  public void Детекция_Шифрования_ПоЗаголовку()
  {
    using MemoryStream aes = BuildAes("pw", out _, out _);
    aes.Position = 0;
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.TryDetectStreamEncryption(aes, out bool enc1));
    Assert.True(enc1);

    // Незашифрованный LZMA2 → не помечается.
    var entries = new List<SevenZipStreamingEntry> { new("f.txt", 5, () => new MemoryStream([1, 2, 3, 4, 5])) };
    using var plain = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, plain, 1 << 20));
    plain.Position = 0;
    Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.TryDetectStreamEncryption(plain, out bool enc2));
    Assert.False(enc2);
  }

  [Fact]
  public void ПотоковоеИзвлечение_Aes_СПаролем_Распаковывается()
  {
    using MemoryStream aes = BuildAes("secret", out byte[] a, out byte[] b);

    string dir = Path.Combine(Path.GetTempPath(), "aesstreamextract", Guid.NewGuid().ToString("N"));
    try
    {
      aes.Position = 0;
      var options = SevenZipDecodeOptions.WithPassword(SevenZipPassword.FromString("secret"));
      Assert.Equal(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectoryFromStream(aes, options, dir, overwrite: false));

      Assert.Equal(a, File.ReadAllBytes(Path.Combine(dir, "a.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(dir, "sub", "b.txt")));
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }

  [Fact]
  public void ПотоковоеИзвлечение_Aes_БезПароля_НеРаспаковывается()
  {
    using MemoryStream aes = BuildAes("secret", out _, out _);

    string dir = Path.Combine(Path.GetTempPath(), "aesstreamnopw", Guid.NewGuid().ToString("N"));
    try
    {
      aes.Position = 0;
      Assert.NotEqual(SevenZipArchiveDecodeResult.Ok,
          SevenZipArchiveDecoder.ExtractToDirectoryFromStream(aes, SevenZipDecodeOptions.Default, dir, overwrite: false));
    }
    finally { try { Directory.Delete(dir, recursive: true); } catch { } }
  }
}
