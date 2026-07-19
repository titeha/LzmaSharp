using System.Text;
using Lzma.Core.SevenZip;
using Xunit;

namespace Lzma.Core.Tests.SevenZip;

/// <summary>
/// Частичное извлечение 7z (предикат shouldExtract): на диск пишутся только выбранные записи. Solid-folder
/// декодируется целиком, но невыбранные подпотоки уходят в Stream.Null; folder без выбранных не декодируется.
/// </summary>
public sealed class SevenZipArchiveDecoderPartialExtractTests
{
  private static string NewTempDir() => Path.Combine(Path.GetTempPath(), "lzs-partial-" + Guid.NewGuid().ToString("N"));

  private static byte[] Text(string s) => Encoding.UTF8.GetBytes(s);

  private static byte[] BuildLzma2(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipArchiveWriterEntry(f.Name, f.Data)).ToArray();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildArchive(entries, SevenZipWriterCompressionMethod.Lzma2, out byte[] bytes));
    return bytes;
  }

  private static byte[] BuildSolid(params (string Name, byte[] Data)[] files)
  {
    var entries = files.Select(f => new SevenZipStreamingEntry(f.Name, f.Data.Length, () => new MemoryStream(f.Data))).ToArray();
    using var ms = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2SolidArchiveToStream(entries, ms, 1 << 20));
    return ms.ToArray();
  }

  private static void ExtractPartialSpan(byte[] archive, Func<string, bool> filter, string dest)
      => Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectory(
          archive, SevenZipDecodeOptions.Default, dest, overwrite: false, out _, null, default, null, filter));

  private static void Run(byte[] archive, Func<string, bool> filter, Action<string> assert)
  {
    string dest = NewTempDir();
    try { ExtractPartialSpan(archive, filter, dest); assert(dest); }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
  }

  [Fact]
  public void Solid_ИзвлекаетТолькоВыбранные_ОстальныеВNull()
  {
    byte[] a = Text("first file content"), b = Text("SECOND file — selected"), c = Text("third file content");
    byte[] archive = BuildSolid(("a.txt", a), ("b.txt", b), ("c.txt", c));

    Run(archive, name => name == "b.txt", dest =>
    {
      Assert.True(File.Exists(Path.Combine(dest, "b.txt")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(dest, "b.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "c.txt")));
    });
  }

  [Fact]
  public void Solid_ВыборНесколькихИзСередины()
  {
    byte[] a = Text("AAAA"), b = Text("BBBB"), c = Text("CCCC"), d = Text("DDDD");
    byte[] archive = BuildSolid(("a", a), ("b", b), ("c", c), ("d", d));
    var pick = new HashSet<string> { "b", "d" };

    Run(archive, pick.Contains, dest =>
    {
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(dest, "b")));
      Assert.Equal(d, File.ReadAllBytes(Path.Combine(dest, "d")));
      Assert.False(File.Exists(Path.Combine(dest, "a")));
      Assert.False(File.Exists(Path.Combine(dest, "c")));
    });
  }

  [Fact]
  public void NonSolid_ПропускаетFolderБезВыбранных()
  {
    byte[] archive = BuildLzma2(("a.txt", Text("alpha")), ("b.txt", Text("bravo")), ("c.txt", Text("charlie")));

    Run(archive, name => name == "a.txt", dest =>
    {
      Assert.Equal(Text("alpha"), File.ReadAllBytes(Path.Combine(dest, "a.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "b.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "c.txt")));
    });
  }

  [Fact]
  public void ВыборПапки_ИзвлекаетПоддерево()
  {
    byte[] x = Text("doc x"), y = Text("doc y"), z = Text("other z");
    byte[] archive = BuildSolid(("docs/x.txt", x), ("docs/y.txt", y), ("other/z.txt", z));

    Run(archive, name => name.StartsWith("docs/", StringComparison.Ordinal), dest =>
    {
      Assert.Equal(x, File.ReadAllBytes(Path.Combine(dest, "docs", "x.txt")));
      Assert.Equal(y, File.ReadAllBytes(Path.Combine(dest, "docs", "y.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "other", "z.txt")));
      Assert.False(Directory.Exists(Path.Combine(dest, "other")));
    });
  }

  [Fact]
  public void ФильтрВсегдаИстина_ИзвлекаетВсё_КакБезФильтра()
  {
    byte[] a = Text("one"), b = Text("two");
    byte[] archive = BuildSolid(("a", a), ("b", b));

    Run(archive, _ => true, dest =>
    {
      Assert.Equal(a, File.ReadAllBytes(Path.Combine(dest, "a")));
      Assert.Equal(b, File.ReadAllBytes(Path.Combine(dest, "b")));
    });
  }

  [Fact]
  public void НичегоНеВыбрано_НичегоНеПишет_НоOk()
  {
    byte[] archive = BuildSolid(("a", Text("x")), ("b", Text("y")));
    Run(archive, _ => false, dest =>
    {
      Assert.False(File.Exists(Path.Combine(dest, "a")));
      Assert.False(File.Exists(Path.Combine(dest, "b")));
    });
  }

  [Fact]
  public void Stream_ЧастичноеИзвлечениеИзФайла()
  {
    // Потоковый writer пишет одиночные LZMA2-folder-ы (по файлу) → stream-извлечение их поддерживает.
    var entries = new[]
    {
        new SevenZipStreamingEntry("keep.txt", 5, () => new MemoryStream(Text("keep!"))),
        new SevenZipStreamingEntry("drop.txt", 5, () => new MemoryStream(Text("drop!"))),
    };
    using var archiveMs = new MemoryStream();
    Assert.Equal(SevenZipArchiveWriteResult.Ok,
        SevenZipArchiveWriter.BuildLzma2ArchiveToStream(entries, archiveMs, 1 << 20));
    archiveMs.Position = 0;

    string dest = NewTempDir();
    try
    {
      Assert.Equal(SevenZipArchiveDecodeResult.Ok, SevenZipArchiveDecoder.ExtractToDirectoryFromStream(
          archiveMs, SevenZipDecodeOptions.Default, dest, overwrite: false, null, default, null,
          name => name == "keep.txt"));

      Assert.Equal(Text("keep!"), File.ReadAllBytes(Path.Combine(dest, "keep.txt")));
      Assert.False(File.Exists(Path.Combine(dest, "drop.txt")));
    }
    finally { if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true); }
  }
}
