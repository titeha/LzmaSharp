using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

public partial class CreatePasswordWindow : Window
{
    public CreatePasswordWindow()
    {
        InitializeComponent();
        Opened += (_, _) => PasswordBox.Focus();
    }

    // ОК: валидируем непустоту и совпадение; иначе показываем ошибку и не закрываем.
    private void OnOk(object? sender, RoutedEventArgs e)
    {
        string password = PasswordBox.Text ?? string.Empty;
        string confirm = ConfirmBox.Text ?? string.Empty;

        if (password.Length == 0)
        {
            ErrorText.Text = "Пароль не должен быть пустым.";
            ErrorText.IsVisible = true;
            return;
        }

        if (password != confirm)
        {
            ErrorText.Text = "Пароли не совпадают.";
            ErrorText.IsVisible = true;
            return;
        }

        Close(password);
    }

    // Отмена → null.
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
