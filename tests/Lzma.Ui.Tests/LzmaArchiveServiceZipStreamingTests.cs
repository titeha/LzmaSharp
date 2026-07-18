using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;
using Lzma.Core.Zip;
using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// Потоковые ZIP-операции сервиса по пути: детект формата, обзор каталога и извлечение из файла
/// (без загрузки архива в память).
/// </summary>
public sealed class LzmaArchiveServiceZipStreamingTests
{
  private static string NewTempPath(string ext)
      => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lzs-svc-" + System.Guid.NewGuid().ToString("N") + ext);

  private static string WriteZip(params ZipWriterEntry[] entries)
  {
    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(entries, out byte[] archive));
    string path = NewTempPath(".zip");
    System.IO.File.WriteAllBytes(path, archive);
    return path;
  }

  [Fact]
  public async Task IsZipFile_РазличаетZipИ7z()
  {
    var service = new LzmaArchiveService();

    string zip = WriteZip(new ZipWriterEntry("a.txt", Encoding.UTF8.GetBytes("hi")));

    SevenZipArchiveWriter.BuildArchive([new SevenZipArchiveWriterEntry("a.txt", Encoding.UTF8.GetBytes("hi"))], out byte[] sevenZip);
    string sz = NewTempPath(".7z");
    System.IO.File.WriteAllBytes(sz, sevenZip);

    try
    {
      Assert.True(await service.IsZipFileAsync(zip));
      Assert.False(await service.IsZipFileAsync(sz));
    }
    finally
    {
      System.IO.File.Delete(zip);
      System.IO.File.Delete(sz);
    }
  }

  [Fact]
  public async Task OpenZipFromFile_ЛиститКаталог()
  {
    string zip = WriteZip(
        new ZipWriterEntry("dir/", [], IsDirectory: true),
        new ZipWriterEntry("dir/readme.txt", Encoding.UTF8.GetBytes("readme")),
        new ZipWriterEntry("big.txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("x", 4000)))));

    try
    {
      var service = new LzmaArchiveService();
      ZipListOutcome outcome = await service.OpenZipFromFileAsync(zip);

      Assert.Equal(ZipReadResult.Ok, outcome.Result);
      Assert.Contains(outcome.Entries, e => e.Name == "dir/" && e.IsDirectory);
      Assert.Contains(outcome.Entries, e => e.Name == "dir/readme.txt" && e.UncompressedSize == 6);
      Assert.Contains(outcome.Entries, e => e.Name == "big.txt" && e.UncompressedSize == 4000);
    }
    finally
    {
      System.IO.File.Delete(zip);
    }
  }

  [Fact]
  public async Task ExtractZipFile_РаспаковываетНаДиск()
  {
    byte[] text = Encoding.UTF8.GetBytes("streaming service extract");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Lorem ipsum. ", 30_000))); // > окна инфлейтера

    string zip = WriteZip(
        new ZipWriterEntry("readme.txt", text),
        new ZipWriterEntry("nested/big.txt", big));

    string dest = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lzs-svcx-" + System.Guid.NewGuid().ToString("N"));
    try
    {
      var service = new LzmaArchiveService();
      ZipExtractResult result = await service.ExtractZipFileAsync(zip, dest);

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.Equal(text, System.IO.File.ReadAllBytes(System.IO.Path.Combine(dest, "readme.txt")));
      Assert.Equal(big, System.IO.File.ReadAllBytes(System.IO.Path.Combine(dest, "nested", "big.txt")));
    }
    finally
    {
      System.IO.File.Delete(zip);
      if (System.IO.Directory.Exists(dest))
        System.IO.Directory.Delete(dest, recursive: true);
    }
  }
}
