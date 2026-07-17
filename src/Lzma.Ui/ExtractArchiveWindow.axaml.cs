using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

// Окно «Извлечь архив»: источник (открытый архив / архив с диска) + прогресс. DataContext — общий
// MainViewModel (переиспользуем его команды/состояние), так что вся логика извлечения остаётся в VM.
public partial class ExtractArchiveWindow : Window
{
    public ExtractArchiveWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
