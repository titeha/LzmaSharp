using Avalonia.Controls;
using Avalonia.Input;

using Lzma.Ui.Models;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
