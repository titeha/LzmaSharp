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

    // Открыть отдельное окно «Создать архив»: настройки сжатия + источник + прогресс.
    // DataContext общий с главным окном (тот же MainViewModel) — вся логика создания уже в VM.
    private void OnOpenCreateWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ShowOperationDialog(new CreateArchiveWindow { DataContext = DataContext });

    // Открыть отдельное окно «Извлечь архив»: источник + прогресс. DataContext общий с главным окном.
    private void OnOpenExtractWindow(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => ShowOperationDialog(new ExtractArchiveWindow { DataContext = DataContext });

    // Показать модальное окно операции. Пока оно открыто, прогресс идёт в нём, а нижняя панель
    // главного окна скрыта (флаг IsOperationWindowActive) — чтобы не дублировать полосу прогресса.
    private void ShowOperationDialog(Window window)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.IsOperationWindowActive = true;
            window.Closed += (_, _) => viewModel.IsOperationWindowActive = false;
        }

        _ = window.ShowDialog(this);
    }

    // Окно «О программе» (логотип, версия, кодеки, лицензия).
    private void OnOpenAbout(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => new AboutWindow().ShowDialog(this);

    // Окно «Информация об архиве»: сводка по ОТКРЫТОМУ или ВЫБРАННОМУ в дереве архиву.
    private async void OnOpenArchiveInfo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && await viewModel.BuildArchiveInfoAsync() is { } info)
            await new ArchiveInfoWindow { DataContext = info }.ShowDialog(this);
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

    // Двойной клик по строке: папка — заход внутрь, файл-архив — открытие (логика в VM).
    private async void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is ListBox { SelectedItem: ArchiveItem item })
        {
            await viewModel.ActivateItemAsync(item);
        }
    }

    // Двойной клик по узлу дерева ФС: файл-архив — открыть (папку раскрывает сам TreeView).
    private async void OnTreeNodeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && sender is TreeView { SelectedItem: TreeNodeItem node })
        {
            await viewModel.ActivateTreeNodeAsync(node);
        }
    }

    // Клик по строке адреса: включить ручной ввод пути, сфокусировать поле и выделить текст.
    private void OnEditPath(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || !viewModel.CanEditPath)
            return;

        viewModel.BeginEditPath();
        if (this.FindControl<TextBox>("PathBox") is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    // Уход фокуса из поля пути — отменяем ввод (возврат к крошкам), если он ещё активен.
    private void OnPathBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.CancelEditPath();
    }
}
