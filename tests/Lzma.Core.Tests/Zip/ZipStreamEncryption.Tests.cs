using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковое шифрование ZIP (WinZip-AES): создать зашифрованный архив → прочитать каталог (детект AES)
/// → извлечь с правильным/неверным/пустым паролем. Интероп с 7-Zip — живой прогон.
/// </summary>
public sealed class ZipStreamEncryptionTests
{
  private static ZipStreamingEntry F(string name, byte[] c)
      => new(name, c.LongLength, () => new MemoryStream(c, writable: false));

  private static string TempDir() => Path.Combine(Path.GetTempPath(), "lzs-zipenc-" + Guid.NewGuid().ToString("N"));

  [Fact]
  public void Зашифровать_Прочитать_Извлечь_RoundTrip()
  {
    byte[] text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("секретный текст ", 500)));  // → Deflate
    var rnd = new Random(3); byte[] noise = new byte[2000]; rnd.NextBytes(noise);                      // → Store
    byte[] password = Encoding.UTF8.GetBytes("пароль-P@ss");

    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok, ZipStreamWriter.Write(
        [F("docs/secret.txt", text), F("blob.bin", noise)], ms,
        progress: null, token: default, currentFile: null, maxDegreeOfParallelism: 0, password: password));

    byte[] archive = ms.ToArray();

    // Каталог: члены помечены зашифрованными.
    using (var read = new MemoryStream(archive, writable: false))
    {
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
      Assert.Equal(2, entries.Length);
      Assert.All(entries, e => Assert.True(e.IsEncrypted));
    }

    // Извлечение с ПРАВИЛЬНЫМ паролем.
    string dest = TempDir();
    try
    {
      using var read = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));
      Assert.Equal(ZipExtractResult.Ok,
          ZipStreamExtractor.ExtractToDirectory(read, entries, dest, overwrite: false, currentFile: null, token: default, progress: null, password: password));

      Assert.Equal(text, File.ReadAllBytes(Path.Combine(dest, "docs", "secret.txt")));
      Assert.Equal(noise, File.ReadAllBytes(Path.Combine(dest, "blob.bin")));
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }

  [Fact]
  public void Извлечь_НеверныйПароль_WrongPassword()
  {
    byte[] data = Encoding.UTF8.GetBytes("данные");
    using var ms = new MemoryStream();
    ZipStreamWriter.Write([F("a.txt", data)], ms, null, default, null, 0, Encoding.UTF8.GetBytes("right"));

    using var read = new MemoryStream(ms.ToArray(), writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));

    string dest = TempDir();
    try
    {
      var r = ZipStreamExtractor.ExtractToDirectory(read, entries, dest, false, null, default, null, Encoding.UTF8.GetBytes("wrong"));
      Assert.Equal(ZipExtractResult.WrongPassword, r);
      Assert.False(Directory.Exists(dest)); // откат
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }

  [Fact]
  public void Извлечь_БезПароля_WrongPassword()
  {
    byte[] data = Encoding.UTF8.GetBytes("данные");
    using var ms = new MemoryStream();
    ZipStreamWriter.Write([F("a.txt", data)], ms, null, default, null, 0, Encoding.UTF8.GetBytes("pw"));

    using var read = new MemoryStream(ms.ToArray(), writable: false);
    Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(read, out ZipStreamEntry[] entries));

    string dest = TempDir();
    try
    {
      var r = ZipStreamExtractor.ExtractToDirectory(read, entries, dest, false, null, default, null, password: null);
      Assert.Equal(ZipExtractResult.WrongPassword, r);
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, true); }
  }
}
