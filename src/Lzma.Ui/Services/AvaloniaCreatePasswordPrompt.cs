using Avalonia.Controls;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="ICreatePasswordPrompt"/> через модальный диалог Avalonia
/// (<see cref="CreatePasswordWindow"/> — поле пароля + подтверждение).
/// </summary>
public sealed class AvaloniaCreatePasswordPrompt(Window owner) : ICreatePasswordPrompt
{
  private readonly Window _owner = owner;

  public async Task<string?> RequestNewPasswordAsync()
  {
    var dialog = new CreatePasswordWindow();
    return await dialog.ShowDialog<string?>(_owner);
  }
}
