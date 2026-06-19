using System.IO.Compression;
using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

public sealed class ZipReaderTests
{
  [Theory]
  [InlineData(CompressionLevel.Optimal)]
  [InlineData(CompressionLevel.NoCompression)]
  public void Read_ЧитаетZipСозданныйBcl(CompressionLevel level)
  {
    var files = new (string Name, byte[] Content)[]
    {
      ("hello.txt", Encoding.UTF8.GetBytes("Hello, ZIP reader!")),
      ("dir/repeated.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("repeat ", 2000)))),
      ("dir/sub/data.bin", MakePseudoRandom(5000, 7)),
      ("пример.txt", Encoding.UTF8.GetBytes("UTF-8 имя файла")),
    };

    byte[] zip = BclCreateZip(files, level);

    ZipReadResult result = ZipReader.Read(zip, out ZipEntry[] entries);

    Assert.Equal(ZipReadResult.Ok, result);

    foreach ((string name, byte[] content) in files)
    {
      ZipEntry entry = Assert.Single(entries, e => e.Name == name && !e.IsDirectory);
      Assert.Equal(content, entry.Bytes);
    }
  }

  [Fact]
  public void Read_ПовреждённыйАрхив_НеПадаетНеобработанно()
  {
    byte[] zip = BclCreateZip([("a.txt", Encoding.UTF8.GetBytes("some content here"))], CompressionLevel.Optimal);
    zip[zip.Length / 2] ^= 0xFF;

    ZipReadResult result = ZipReader.Read(zip, out _);

    Assert.True(result is ZipReadResult.Ok or ZipReadResult.InvalidData or ZipReadResult.NotSupported);
  }

  [Fact]
  public void Read_НеZip_ВозвращаетInvalidData()
  {
    Assert.Equal(ZipReadResult.InvalidData, ZipReader.Read(new byte[100], out _));
  }

  private static byte[] BclCreateZip((string Name, byte[] Content)[] files, CompressionLevel level)
  {
    using var ms = new MemoryStream();
    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
    {
      foreach ((string name, byte[] content) in files)
      {
        ZipArchiveEntry entry = zip.CreateEntry(name, level);
        using Stream s = entry.Open();
        s.Write(content, 0, content.Length);
      }
    }

    return ms.ToArray();
  }

  private static byte[] MakePseudoRandom(int length, int seed)
  {
    var random = new Random(seed);
    byte[] data = new byte[length];
    random.NextBytes(data);
    return data;
  }
}
