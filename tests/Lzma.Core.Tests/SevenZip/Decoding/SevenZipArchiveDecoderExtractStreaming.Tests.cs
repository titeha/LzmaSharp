using System.IO;
using System.Linq;
using System.Text;

using Lzma.Core.SevenZip;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// End-to-end тесты потокового извлечения на диск (folder-за-folder-ом, без накопления всего
/// архива в памяти): корректность содержимого файлов и атомарность (при порче данных — сбой и
/// откат: на диске ничего не остаётся).
/// </summary>
public sealed class SevenZipArchiveDecoderExtractStreamingTests
{
  private static string CreateTempRoot()
      => Path.Combine(Path.GetTempPath(), "LzmaExtractStreaming", Guid.NewGuid().ToString("N"));

  private static void TryDeleteTree(string dir)
  {
    try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    catch { /* best-effort */ }
  }

  [Fact]
  public void ПотоковоеИзвлечение_МногоФайлов_ВложеннаяПапка_БольшойФайл_ПишетБайтВБайт()
  {
    byte[] a = Encoding.UTF8.GetBytes("привет");
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Большой поток 0123456789 ", 8000))); // многочанковый
    byte[] b = Encoding.UTF8.GetBytes("мир");

    SevenZipArchiveWriterEntry[] entries =
    [
        new("dir", [], IsDirectory: true),
        new("a.txt", a),
        new("empty.txt", []),
        new("dir/big.bin", big),
        new("b.txt", b),
    ];

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    string root = CreateTempRoot();
    try
    {
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectory(
          archive, SevenZipDecodeOptions.Default, root, overwrite: false, out _));

      Assert.True(Directory.Exists(Path.Combine(root, "dir")));
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(root, "a.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(root, "b.txt")));
      Assert.Equal(big, File.ReadAllBytes(Path.Combine(root, "dir", "big.bin")));
      Assert.True(File.Exists(Path.Combine(root, "empty.txt")));
      Assert.Empty(File.ReadAllBytes(Path.Combine(root, "empty.txt")));
    }
    finally
    {
      TryDeleteTree(root);
    }
  }

  [Fact]
  public void ПотоковоеИзвлечение_ПорченыеДанные_InvalidData_ИНичегоНеОстаётся()
  {
    byte[] big = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("Целостность 0123456789 ", 8000)));

    Assert.Equal(SevenZipArchiveWriteResult.Ok, SevenZipArchiveWriter.BuildArchive(
        [new SevenZipArchiveWriterEntry("big.bin", big)],
        SevenZipWriterCompressionMethod.Lzma2, out byte[] archive));

    // Портим байт в области packed-данных (сразу после 32-байтной сигнатуры) — декод даст
    // неверные байты → несовпадение CRC → InvalidData, а частично записанный файл откатится.
    archive[40] ^= 0xFF;

    string root = CreateTempRoot();
    try
    {
      SevenZipArchiveDecodeResult result = SevenZipArchiveDecoder.ExtractToDirectory(
          archive, SevenZipDecodeOptions.Default, root, overwrite: false, out _);

      Assert.Equal(SevenZipArchiveDecodeResult.InvalidData, result);
      Assert.False(File.Exists(Path.Combine(root, "big.bin"))); // частичный файл откачен
      Assert.False(Directory.Exists(root));                     // целевая папка тоже
    }
    finally
    {
      TryDeleteTree(root);
    }
  }
}
