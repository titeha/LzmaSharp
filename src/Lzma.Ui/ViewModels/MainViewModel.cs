using MvvmUtilites;

namespace Lzma.Ui.ViewModels;

/// <summary>
/// Главная модель представления окна архиватора.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
  /// <summary>Базовый заголовок окна, когда архив не открыт.</summary>
  public const string DefaultTitle = "LzmaSharp — архиватор";

  private string _title = DefaultTitle;

  /// <summary>
  /// Заголовок окна. Позже станет динамическим: «имя_архива — LzmaSharp» при открытом архиве.
  /// </summary>
  public string Title
  {
    get => _title;
    set => Set(ref _title, value);
  }
}
