using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Lzma.Ui;

// Окно «Создать архив»: настройки сжатия + источник + прогресс. DataContext — общий MainViewModel
// (переиспользуем его команды/состояние), так что вся логика создания остаётся в VM.
public partial class CreateArchiveWindow : Window
{
    public CreateArchiveWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
