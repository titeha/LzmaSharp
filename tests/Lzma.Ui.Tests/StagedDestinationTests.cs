using Lzma.Ui.Services;

namespace Lzma.Ui.Tests;

/// <summary>
/// SEC-002 (§4.4 шаг 2): юнит-тесты вычисления staged-пути в <see cref="StagedDestination"/>.
/// Конструктор файлов на диске не создаёт — проверяется только расчёт пути.
/// </summary>
public sealed class StagedDestinationTests
{
  [Fact]
  public void Конструктор_ПустойПуть_БросаетArgumentException()
  {
    ArgumentException ex = Assert.Throws<ArgumentException>(() => new StagedDestination(""));

    Assert.Equal("destinationPath", ex.ParamName);
  }

  [Fact]
  public void Конструктор_ПутьИзПробелов_БросаетArgumentException()
  {
    ArgumentException ex = Assert.Throws<ArgumentException>(() => new StagedDestination("   "));

    Assert.Equal("destinationPath", ex.ParamName);
  }

  [Fact]
  public void StagedPath_ЛежитВТомЖеКаталогеЧтоИНазначение()
  {
    string dir = Path.Combine(Path.GetTempPath(), "lzmasharp-staged-" + Guid.NewGuid().ToString("N"));
    string destination = Path.Combine(dir, "archive.7z");

    var staged = new StagedDestination(destination);

    // Тот же каталог = та же файловая система: коммит возможен переносом без копирования.
    Assert.Equal(dir, Path.GetDirectoryName(staged.StagedPath));
  }

  [Fact]
  public void StagedPath_СодержитИмяФайлаНазначения()
  {
    string destination = Path.Combine(Path.GetTempPath(), "archive.7z");

    var staged = new StagedDestination(destination);

    Assert.Contains("archive.7z", Path.GetFileName(staged.StagedPath));
  }

  [Fact]
  public void StagedPath_УникаленМеждуЭкземплярами()
  {
    string destination = Path.Combine(Path.GetTempPath(), "archive.7z");

    var first = new StagedDestination(destination);
    var second = new StagedDestination(destination);

    Assert.NotEqual(first.StagedPath, second.StagedPath);
  }

  [Fact]
  public void StagedPath_НеСовпадаетСНазначением()
  {
    string destination = Path.Combine(Path.GetTempPath(), "archive.7z");

    var staged = new StagedDestination(destination);

    Assert.NotEqual(destination, staged.StagedPath);
  }

  [Fact]
  public void StagedPath_ОтносительныйПутьБезКаталога_ИспользуетТекущийКаталог()
  {
    var staged = new StagedDestination("archive.7z");

    Assert.Equal(".", Path.GetDirectoryName(staged.StagedPath));
  }
}
