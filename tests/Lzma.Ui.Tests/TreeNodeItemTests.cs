using Lzma.Ui.Models;

namespace Lzma.Ui.Tests;

/// <summary>
/// Модель узла дерева браузера: ленивая догрузка детей при раскрытии, заглушка до догрузки, выбор,
/// производные (значок/тип/размер).
/// </summary>
public sealed class TreeNodeItemTests
{
  private static TreeNodeItem Dir(string name, System.Func<TreeNodeItem, IReadOnlyList<TreeNodeItem>>? loader = null)
  {
    var node = new TreeNodeItem(loader) { Name = name, IsDirectory = true, Size = 0, FullPath = "X:\\" + name };
    node.AddLoadingPlaceholder();
    return node;
  }

  private static TreeNodeItem File(string name, long size = 10)
      => new() { Name = name, IsDirectory = false, Size = size, FullPath = "X:\\" + name };

  [Fact]
  public void Папка_ДоРаскрытия_ИмеетЗаглушку_НеЗагружена()
  {
    var node = Dir("docs", _ => [File("a.txt")]);
    Assert.False(node.IsLoaded);
    Assert.Single(node.Children); // заглушка (для стрелки раскрытия)
    Assert.Equal(string.Empty, node.Children[0].Name);
  }

  [Fact]
  public void Раскрытие_ЛенивоЗагружаетДетей_ЗаменяетЗаглушку()
  {
    int calls = 0;
    var node = Dir("docs", _ => { calls++; return [File("a.txt"), Dir("sub")]; });

    node.IsExpanded = true;

    Assert.True(node.IsLoaded);
    Assert.Equal(1, calls);
    Assert.Equal(2, node.Children.Count);
    Assert.Equal("a.txt", node.Children[0].Name);
    Assert.Equal("sub", node.Children[1].Name);
  }

  [Fact]
  public void ПовторноеРаскрытие_НеЗагружаетСнова()
  {
    int calls = 0;
    var node = Dir("docs", _ => { calls++; return [File("a.txt")]; });

    node.IsExpanded = true;
    node.IsExpanded = false;
    node.IsExpanded = true;

    Assert.Equal(1, calls); // загрузка один раз
  }

  [Fact]
  public void БезЗагрузчика_Раскрытие_НеПадает()
  {
    var node = Dir("empty", loader: null);
    node.IsExpanded = true; // не должно бросить
    Assert.True(node.IsExpanded);
  }

  [Fact]
  public void Выбор_Наблюдаемый()
  {
    var node = File("a.txt");
    bool raised = false;
    node.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(TreeNodeItem.IsSelected)) raised = true; };
    node.IsSelected = true;
    Assert.True(raised);
    Assert.True(node.IsSelected);
  }

  [Theory]
  [InlineData("photo.jpg", false, "файл")]
  [InlineData("data.7z", true, "архив")]
  [InlineData("pack.zip", true, "архив")]
  public void Производные_ЗначокИТип(string name, bool isArchive, string kind)
  {
    var node = File(name);
    Assert.Equal(isArchive, node.IsArchiveFile);
    Assert.Equal(!isArchive, node.IsPlainFile);
    Assert.Equal(kind, node.Kind);
    Assert.NotEqual(string.Empty, node.DisplaySize); // файл — есть размер
  }

  [Fact]
  public void Папка_РазмерПусто()
  {
    var node = Dir("d");
    Assert.Equal(string.Empty, node.DisplaySize);
    Assert.Equal("папка", node.Kind);
  }
}
