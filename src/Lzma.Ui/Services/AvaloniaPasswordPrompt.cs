using Avalonia.Controls;

namespace Lzma.Ui.Services;

/// <summary>
/// Реализация <see cref="IPasswordPrompt"/> через модальный диалог Avalonia.
/// </summary>
public sealed class AvaloniaPasswordPrompt(Window owner) : IPasswordPrompt
{
  private readonly Window _owner = owner;

  public async Task<string?> RequestAsync(string archiveName, bool previousAttemptFailed)
  {
    var dialog = new PasswordWindow(archiveName, previousAttemptFailed);
    return await dialog.ShowDialog<string?>(_owner);
  }
}
