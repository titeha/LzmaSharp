using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;

using Lzma.Ui.Models;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Переключение тёмная/светлая тема на лету.
    private void OnToggleTheme(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant =
                app.ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        }
    }

    // Двойной клик по строке: для папки — перейти внутрь (навигация живёт в VM).
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is ListBox { SelectedItem: ArchiveItem item })
        {
            viewModel.NavigateInto(item);
        }
    }
}
