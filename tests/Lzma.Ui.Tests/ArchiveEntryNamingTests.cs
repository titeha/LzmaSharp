using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

public sealed class ArchiveEntryNamingTests
{
  [Fact]
  public void Файл_ВКорнеПапки_ИмяПапкиПлюсФайл()
  {
    string rootFolder = OperatingSystem.IsWindows() ? @"C:\data\docs" : "/data/docs";
    string file = OperatingSystem.IsWindows() ? @"C:\data\docs\a.txt" : "/data/docs/a.txt";
    string name = ArchiveEntryNaming.ForFileUnderFolder(rootFolder, file);
    Assert.Equal("docs/a.txt", name);
  }

  [Fact]
  public void Файл_ВПодпапке_ОтносительныйПутьЧерезСлеш()
  {
    string rootFolder = OperatingSystem.IsWindows() ? @"C:\data\docs" : "/data/docs";
    string file = OperatingSystem.IsWindows() ? @"C:\data\docs\sub\deep\b.txt" : "/data/docs/sub/deep/b.txt";
    string name = ArchiveEntryNaming.ForFileUnderFolder(rootFolder, file);
    Assert.Equal("docs/sub/deep/b.txt", name);
  }

  [Fact]
  public void КореньСЗавершающимСлешем_НеЛомаетИмя()
  {
    string rootFolder = OperatingSystem.IsWindows() ? @"C:\data\docs\" : "/data/docs/";
    string file = OperatingSystem.IsWindows() ? @"C:\data\docs\sub\b.txt" : "/data/docs/sub/b.txt";
    string name = ArchiveEntryNaming.ForFileUnderFolder(rootFolder, file);
    Assert.Equal("docs/sub/b.txt", name);
  }

  /// <summary>
  /// Платформенная семантика обратного слеша в имени записи архива.
  /// На Windows '\' — разделитель пути, поэтому <c>ForFileUnderFolder</c> нормализует его в '/'.
  /// На POSIX '\' — легальный символ имени файла, и нормализация НЕ выполняется: иначе файл
  /// <c>a\b.txt</c> попадал бы в архив как <c>a/b.txt</c>, создавая подкаталог, которого
  /// у пользователя нет. Тест закрепляет обе семантики и защищает от регрессии к безусловной
  /// замене разделителя.
  /// </summary>
  [Fact]
  public void ОбратныйСлешВИмениФайла_ПлатформеннаяСемантика()
  {
    // Входы платформенно-эквивалентны, но не одинаковы как строки: на Windows путь содержит
    // разделители '\', на POSIX — разделители '/' и литеральный '\' внутри имени файла.
    // Windows: C:\data\docs + C:\data\docs\a\b.txt → relative = "a\b.txt" → "docs/a/b.txt".
    // POSIX:   /data/docs  + /data/docs/a\b.txt   → relative = "a\b.txt" → "docs/a\b.txt".
    string rootFolder = OperatingSystem.IsWindows() ? @"C:\data\docs" : "/data/docs";
    string file = OperatingSystem.IsWindows() ? @"C:\data\docs\a\b.txt" : "/data/docs/a\\b.txt";

    string name = ArchiveEntryNaming.ForFileUnderFolder(rootFolder, file);

    if (OperatingSystem.IsWindows())
    {
      // На Windows '\' — разделитель, нормализация в '/' корректна.
      Assert.Equal("docs/a/b.txt", name);
    }
    else
    {
      // На POSIX '\' — часть имени файла, поэтому сохраняется как есть.
      Assert.Equal("docs/a\\b.txt", name);
    }
  }
}
