using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

public sealed class ArchiveEntryNamingTests
{
  [Fact]
  public void Файл_ВКорнеПапки_ИмяПапкиПлюсФайл()
  {
    string name = ArchiveEntryNaming.ForFileUnderFolder(@"C:\data\docs", @"C:\data\docs\a.txt");
    Assert.Equal("docs/a.txt", name);
  }

  [Fact]
  public void Файл_ВПодпапке_ОтносительныйПутьЧерезСлеш()
  {
    string name = ArchiveEntryNaming.ForFileUnderFolder(@"C:\data\docs", @"C:\data\docs\sub\deep\b.txt");
    Assert.Equal("docs/sub/deep/b.txt", name);
  }

  [Fact]
  public void КореньСЗавершающимСлешем_НеЛомаетИмя()
  {
    string name = ArchiveEntryNaming.ForFileUnderFolder(@"C:\data\docs\", @"C:\data\docs\sub\b.txt");
    Assert.Equal("docs/sub/b.txt", name);
  }
}
