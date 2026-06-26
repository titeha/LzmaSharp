using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Lzma.Ui.Services;
using Lzma.Ui.ViewModels;

namespace Lzma.Ui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var picker = new AvaloniaArchivePicker(window);
            var passwordPrompt = new AvaloniaPasswordPrompt(window);
            var folderPicker = new AvaloniaFolderPicker(window);
            var sourceFilesPicker = new AvaloniaSourceFilesPicker(window);
            var saveFilePicker = new AvaloniaSaveFilePicker(window);
            window.DataContext = new MainViewModel(
                picker,
                passwordPrompt,
                folderPicker,
                new LzmaArchiveService(),
                sourceFilesPicker,
                saveFilePicker);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}