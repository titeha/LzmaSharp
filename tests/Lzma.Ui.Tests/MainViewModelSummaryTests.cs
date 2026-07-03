using Lzma.Ui.ViewModels;

namespace Lzma.Ui.Tests;

/// <summary>
/// Тесты итогового сообщения после создания архива: путь + исходный/итоговый размер и
/// коэффициент сжатия (либо только итоговый размер, если исходный неизвестен/пуст).
/// </summary>
public sealed class MainViewModelSummaryTests
{
  [Fact]
  public void FormatCreateSummary_СРазмерами_ПоказываетИтогИКоэффициент()
  {
    string s = MainViewModel.FormatCreateSummary("C:\\out.7z", 10 * 1024 * 1024, 2 * 1024 * 1024);

    Assert.Contains("Создан архив: C:\\out.7z", s);
    Assert.Contains("было", s);
    Assert.Contains("стало", s);
    Assert.Contains("×", s);          // коэффициент присутствует
    Assert.Contains("10 МБ", s);      // исходный
    Assert.Contains("2 МБ", s);       // итоговый
  }

  [Fact]
  public void FormatCreateSummary_НулевойИсходныйРазмер_ТолькоИтоговый()
  {
    string s = MainViewModel.FormatCreateSummary("out.7z", originalBytes: 0, compressedBytes: 32);

    Assert.Equal("Создан архив: out.7z (32 Б)", s);
    Assert.DoesNotContain("было", s);
  }
}
