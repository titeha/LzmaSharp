using System.Reflection;

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        // Версия приложения из сборки (X.Y.Z).
        System.Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = version is null
            ? string.Empty
            : $"версия {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
