using System.Text;
using Lzma.Core.Zip;
using Xunit;

namespace Lzma.Core.Tests.Zip;

/// <summary>
/// Потоковый путь записи БОЛЬШОГО зашифрованного члена (> порога): Deflate → потоковый WinZip-AES
/// (CTR+HMAC) → seek-патч. Порог понижен, чтобы прогнать путь на маленьких файлах. Сверка нашим
/// потоковым ридером/экстрактором (пароль верный/неверный) + ветка ZIP64.
/// </summary>
public sealed class ZipStreamWriterLargeEncryptedTests
{
  private static ZipStreamingEntry Entry(string name, byte[] data)
      => new(name, data.Length, () => new MemoryStream(data, writable: false));

  private static byte[] WriteEncrypted(IReadOnlyList<ZipStreamingEntry> entries, string password, long largeThreshold, long zip64SizeThreshold)
  {
    byte[] pw = Encoding.UTF8.GetBytes(password);
    using var ms = new MemoryStream();
    Assert.Equal(ZipWriteResult.Ok,
        ZipStreamWriter.Write(entries, ms, largeThreshold, zip64SizeThreshold, null, default, null, 0, pw));
    return ms.ToArray();
  }

  private static byte[] Compressible(int n)
      => Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("encrypted streaming data ", n / 25 + 1)).Substring(0, n));

  private static void ExtractAndAssert(byte[] archive, string password, params (string Name, byte[] Data)[] expected)
  {
    string dest = Path.Combine(Path.GetTempPath(), "lzs-zwenc-" + Guid.NewGuid().ToString("N"));
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));
      Assert.All(entries, e => Assert.True(e.IsEncrypted));

      byte[] pw = Encoding.UTF8.GetBytes(password);
      Assert.Equal(ZipExtractResult.Ok,
          ZipStreamExtractor.ExtractToDirectory(ms, entries, dest, overwrite: false, null, default, null, pw));

      foreach ((string name, byte[] data) in expected)
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar))));
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
  }

  [Fact]
  public void БольшойПуть_Шифрование_RoundTrip()
  {
    byte[] data = Compressible(6000);
    byte[] archive = WriteEncrypted([Entry("big/secret.txt", data)], "p@ss", largeThreshold: 10, zip64SizeThreshold: long.MaxValue);
    ExtractAndAssert(archive, "p@ss", ("big/secret.txt", data));
  }

  [Fact]
  public void БольшойПуть_НесжимаемыйToo()
  {
    var rnd = new Random(3);
    byte[] data = new byte[7000];
    rnd.NextBytes(data);
    byte[] archive = WriteEncrypted([Entry("noise.bin", data)], "pw", largeThreshold: 10, zip64SizeThreshold: long.MaxValue);
    ExtractAndAssert(archive, "pw", ("noise.bin", data));
  }

  [Fact]
  public void БольшойПуть_Шифрование_Zip64Размеры()
  {
    byte[] data = Compressible(5000);
    byte[] archive = WriteEncrypted([Entry("z.txt", data)], "pass", largeThreshold: 10, zip64SizeThreshold: 10);
    ExtractAndAssert(archive, "pass", ("z.txt", data));
  }

  [Fact]
  public void БольшойПуть_НеверныйПароль()
  {
    byte[] data = Compressible(4000);
    byte[] archive = WriteEncrypted([Entry("s.txt", data)], "correct", largeThreshold: 10, zip64SizeThreshold: long.MaxValue);

    string dest = Path.Combine(Path.GetTempPath(), "lzs-zwenc-wp-" + Guid.NewGuid().ToString("N"));
    try
    {
      using var ms = new MemoryStream(archive, writable: false);
      Assert.Equal(ZipReadResult.Ok, ZipStreamReader.ReadCentralDirectory(ms, out ZipStreamEntry[] entries));
      byte[] wrong = Encoding.UTF8.GetBytes("WRONG");
      Assert.Equal(ZipExtractResult.WrongPassword,
          ZipStreamExtractor.ExtractToDirectory(ms, entries, dest, overwrite: false, null, default, null, wrong));
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
  }

  [Fact]
  public void Смешанный_МелкийВолнойИБольшойПотоком_Шифрование()
  {
    byte[] small = Compressible(200);   // ≤ порог → параллельная волна (in-memory шифрование)
    byte[] big = Compressible(5000);    // > порог → потоковый зашифрованный путь
    byte[] archive = WriteEncrypted([Entry("small.txt", small), Entry("big.txt", big)], "k", largeThreshold: 1000, zip64SizeThreshold: long.MaxValue);
    ExtractAndAssert(archive, "k", ("small.txt", small), ("big.txt", big));
  }
}
