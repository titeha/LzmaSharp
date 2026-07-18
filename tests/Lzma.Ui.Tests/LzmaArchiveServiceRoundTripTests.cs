using System.Text;
using Lzma.Core.SevenZip;
using Lzma.Ui.Services;
using Xunit;

namespace Lzma.Ui.Tests;

public sealed class LzmaArchiveServiceRoundTripTests
{
  private static SevenZipStreamingEntry F(string name, byte[] c)
      => new(name, c.LongLength, () => new MemoryStream(c, writable: false));

  [Theory]
  [InlineData(SevenZipWriterCompressionMethod.Auto, 1 << 20)]
  [InlineData(SevenZipWriterCompressionMethod.Auto, 256 << 20)]
  [InlineData(SevenZipWriterCompressionMethod.Lzma2, 256 << 20)]
  [InlineData(SevenZipWriterCompressionMethod.Lzma2, 64 << 20)]
  public void СоздатьОткрыть_РазныеСловари(SevenZipWriterCompressionMethod method, int dict)
  {
    var svc = new LzmaArchiveService();
    var list = new List<SevenZipStreamingEntry>();
    for (int i = 0; i < 10; i++)
    {
      byte[] t = Encoding.UTF8.GetBytes($"файл {i} " + string.Concat(Enumerable.Repeat("текст ", 100)));
      list.Add(F($"f{i}.txt", t));
    }

    string dir = Path.Combine(Path.GetTempPath(), "lzs-dict-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, "out.7z");
    try
    {
      var cr = svc.CreateArchiveToFileAsync(list, path, method, dict).Result;
      Assert.Equal(SevenZipArchiveWriteResult.Ok, cr);
      var opened = svc.OpenAsync(File.ReadAllBytes(path), null).Result;
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, opened.Result);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
  }
}
