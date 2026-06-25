using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

public partial class PasswordWindow : Window
{
    public PasswordWindow()
    {
        InitializeComponent();
    }

    public PasswordWindow(string archiveName, bool previousAttemptFailed)
        : this()
    {
        PromptText.Text = $"Введите пароль для «{archiveName}»";
        ErrorText.IsVisible = previousAttemptFailed;
        Opened += (_, _) => PasswordBox.Focus();
    }

    // Возвращаем введённый пароль (пустая строка допустима — это «пустой пароль»).
    private void OnOk(object? sender, RoutedEventArgs e) => Close(PasswordBox.Text ?? string.Empty);

    // Отмена → null.
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
