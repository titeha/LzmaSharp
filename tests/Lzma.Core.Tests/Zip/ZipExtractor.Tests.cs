using System.Text;

using Lzma.Core.Zip;

namespace Lzma.Core.Tests.Zip;

public sealed class ZipExtractorTests
{
  // Создаёт временную папку и гарантированно удаляет её после теста.
  private static string NewTempDir()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzs-zipx-" + Guid.NewGuid().ToString("N"));
    return dir;
  }

  [Fact]
  public void ExtractToDirectory_ФайлыИПапки_ПишутсяНаДиск()
  {
    byte[] hello = Encoding.UTF8.GetBytes("Hello, extractor!");
    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("x", 3000)));

    ZipEntry[] entries =
    [
      new("dir/", [], IsDirectory: true),
      new("dir/readme.txt", hello, IsDirectory: false),
      new("dir/sub/data.bin", data, IsDirectory: false),
    ];

    string dest = NewTempDir();
    try
    {
      ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest);

      Assert.Equal(ZipExtractResult.Ok, result);
      Assert.True(Directory.Exists(Path.Combine(dest, "dir")));
      Assert.Equal(hello, File.ReadAllBytes(Path.Combine(dest, "dir", "readme.txt")));
      Assert.Equal(data, File.ReadAllBytes(Path.Combine(dest, "dir", "sub", "data.bin")));
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  [Fact]
  public void ExtractToDirectory_ВыходЗаПределыПапки_ОтклоняетсяИНичегоНеОстаётся()
  {
    // Классический zip-slip: путь с "..".
    ZipEntry[] entries =
    [
      new("ok.txt", Encoding.UTF8.GetBytes("safe"), IsDirectory: false),
      new("../evil.txt", Encoding.UTF8.GetBytes("escape"), IsDirectory: false),
    ];

    string parent = NewTempDir();
    string dest = Path.Combine(parent, "out");
    Directory.CreateDirectory(parent);
    try
    {
      ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      // Откат: целевая папка не должна остаться (её создали и удалили), «злой» файл не записан.
      Assert.False(File.Exists(Path.Combine(parent, "evil.txt")));
      Assert.False(Directory.Exists(dest));
    }
    finally
    {
      if (Directory.Exists(parent))
        Directory.Delete(parent, recursive: true);
    }
  }

  [Fact]
  public void ExtractToDirectory_СуществующийФайлБезОверрайта_Отклоняется()
  {
    string dest = NewTempDir();
    Directory.CreateDirectory(dest);
    File.WriteAllText(Path.Combine(dest, "a.txt"), "existing");
    try
    {
      ZipEntry[] entries = [new("a.txt", Encoding.UTF8.GetBytes("new"), IsDirectory: false)];

      ZipExtractResult result = ZipExtractor.ExtractToDirectory(entries, dest, overwrite: false);

      Assert.Equal(ZipExtractResult.InvalidData, result);
      Assert.Equal("existing", File.ReadAllText(Path.Combine(dest, "a.txt")));
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  [Fact]
  public void ExtractToDirectory_РаспаковкаZipОтWriter_ByteВByte()
  {
    // Полный round-trip: writer → reader → extractor → сверка с диском.
    byte[] a = Encoding.UTF8.GetBytes("first file contents");
    byte[] b = MakePseudoRandom(4096, 11);

    Assert.Equal(ZipWriteResult.Ok, ZipWriter.Build(
        [
          new ZipWriterEntry("folder/a.txt", a),
          new ZipWriterEntry("folder/bin/b.dat", b),
        ],
        out byte[] zip));

    Assert.Equal(ZipReadResult.Ok, ZipReader.Read(zip, out ZipEntry[] entries));

    string dest = NewTempDir();
    try
    {
      Assert.Equal(ZipExtractResult.Ok, ZipExtractor.ExtractToDirectory(entries, dest));

      Assert.Equal(a, File.ReadAllBytes(Path.Combine(dest, "folder", "a.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(dest, "folder", "bin", "b.dat")));
    }
    finally
    {
      if (Directory.Exists(dest))
        Directory.Delete(dest, recursive: true);
    }
  }

  private static byte[] MakePseudoRandom(int length, int seed)
  {
    byte[] buffer = new byte[length];
    uint state = (uint)seed;
    for (int i = 0; i < length; i++)
    {
      state = state * 1664525u + 1013904223u;
      buffer[i] = (byte)(state >> 24);
    }

    return buffer;
  }
}
