using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

public partial class ArchiveInfoWindow : Window
{
    public ArchiveInfoWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
